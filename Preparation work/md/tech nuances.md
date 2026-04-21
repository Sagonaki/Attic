6\. Implementation Nuances
==========================
Tech stack .net 10.

6.1 Database (PostgreSQL + EF Core)
-----------------------------------

### 6.1.1 Performance Patterns

To keep the application responsive under load, implement the following EF Core patterns:

*   **Split Queries**: Use .AsSplitQuery() when loading a room with many members or deep history to avoid "Cartesian Explosion" caused by multiple JOINs.
    
*   **Global Query Filters**: Implement filters for DeletedAt (Rooms) and Banned status (RoomMembers). This ensures business logic doesn't have to manually check these flags on every query.
    
*   **Interceptors**: Use a SaveChangesInterceptor to automatically handle UpdatedAt timestamps and generate audit logs for admin actions (bans/deletions).
    
*   **No-Tracking**: Use .AsNoTracking() for read-only operations, such as the public room catalog and message history scrolling, to reduce memory overhead and CPU cycles.
    

### 6.1.2 Recommended Indexes

**TargetIndex ColumnsPurposeMessages**(RoomId, CreatedAt DESC) or (RoomId, Id DESC)Critical for "Infinite Scroll." Allows the DB to fetch the next page of messages without scanning the entire table.**Messages**(SenderId, RecipientId, CreatedAt DESC)Speeds up 1-to-1 DM history loading via a composite index.**Users**Email, Username (Unique Hash Index)Provides O(1)_O_(1) equality checks for logins and friend requests (PostgreSQL specific).**RoomMembers**(RoomId, UserId) (Composite)Prevents full table scans when verifying if a user has permission to post in a room.**Sessions**(UserId, IsActive)Fast lookup for validating multi-tab sessions or managing active logins.

### 6.1.3 Indexes to Avoid

*   **Large Text Columns**: Do not index Message.Content. This causes index bloat and slows down writes. Use **Full-Text Search (GIN)** only if a search feature is required.
    
*   **Low Cardinality Columns**: Avoid indexing columns like IsEdited, IsPrivate, or Gender. The DB engine will likely ignore these in favor of a full table scan.
    
*   **Frequently Updated Timestamps**: Avoid indexing LastSeenAt in the Users table if updated every minute. This causes constant disk I/O. Use a cache (e.g., Redis) instead.
    
*   **Redundant Overlap**: If an index exists on (RoomId, CreatedAt), do **not** create a separate index on just RoomId.
    

### 6.1.4 Advanced Indexing

*   **Filtered Indexes**: For the room catalog, use .HasIndex(r => r.Name).HasFilter("DeletedAt IS NULL") to keep the index size small.
    
*   **Covering Indexes**: Use .Include() in your SQL index for the room catalog (e.g., including Description) so the DB can return data directly from the index without hitting the main table.
    

6.2 Application Layer (ASP.NET Core & SignalR)
----------------------------------------------

### 6.2.1 Infrastructure & Connection Management

*   **Kestrel & Reverse Proxy**:
    
    *   **Problem**: Default limits on concurrent connections in Kestrel or Nginx/YARP can throttle users.
        
    *   **Solution**: Set KestrelServerOptions.Limits.MaxConcurrentConnections to at least 2,000 and ensure the Docker container has a high ulimit for open file descriptors (nofile).
        
*   **The "Multi-Tab" Problem**:
    
    *   **Problem**: Mapping a UserId to a single ConnectionId means only one tab receives messages.
        
    *   **Solution**: Use SignalR **Groups**. When a user connects, add their connection to a group named User\_{UserId}. Send personal messages to the group rather than an individual ID.
        

### 6.2.2 Performance & Threading

*   **Thread Pool Starvation**:
    
    *   **Problem**: Blocking calls (.Result or .Wait()) in a high-I/O chat app cause lag despite low CPU usage.
        
    *   **Solution**: Use async/await throughout the entire stack. For AFK checks, avoid while(true) loops; use System.Threading.Timer or a HostedService.
        
*   **Memory Pressure (LOH)**:
    
    *   **Problem**: Loading thousands of messages into a List creates Large Object Heap (LOH) fragmentation.
        
    *   **Solution**: Use IAsyncEnumerable to stream data from EF Core to the client and use lightweight MessageDto objects instead of full entity models.
        

### 6.2.3 SignalR Optimization

*   **Buffer Limits**:
    
    *   **Problem**: Large text or metadata may exceed the default 32KB SignalR buffer.
        
    *   **Solution**: Increase MaximumReceiveMessageSize to 64KB or 128KB.
        
    *   **Critical**: Do **not** send files (e.g., 20MB uploads) over SignalR. Use a standard Web API Controller with multipart upload.
        
*   **Authentication Bottlenecks**:
    
    *   **Problem**: Validating JWTs on every heartbeat is CPU intensive.
        
    *   **Solution**: SignalR validates tokens during the initial handshake. For real-time bans, use a "Ban" event to remove the user from the SignalR Group and call Context.Abort().
        

6.3 Reliability & Error Handling
--------------------------------

### 6.3.1 Global Exception Handling

*   **Problem**: An unhandled exception in a Hub method kills the connection for that specific user/tab.
    
*   **Solution**: Implement a HubFilter to catch errors globally and return a "FriendlyError" message to the client without dropping the socket.
    

### 6.3.2 Message Persistence & Delivery

*   **Problem**: SignalR does not natively retry failed messages if a connection drops mid-send.
    
*   **Solution**: Implement **Client-Side Acknowledgments**. The UI should show a "sending" state until the server confirms the message is saved to the DB and broadcasted.
    

6.4 Summary of Bottlenecks & Solutions
--------------------------------------

**Potential BottleneckTechnical CauseOptimized SolutionConnection Exhaustion**High tab-to-user ratioUse **SignalR with Redis Backplane** to manage state.**Presence Latency**Constant DB writes for AFK statusKeep presence in **Redis/MemoryCache**; persist to DB only on logout.**Infinite Scroll Lag**OFFSET/FETCH on large setsUse **Keyset Pagination** (WHERE Id < LastSeenId).**File I/O Blocking**Large uploads blocking threadsUse System.IO.Pipelines for streaming uploads to disk.**Multi-Tab AFK Logic**Conflicting active/idle statesUse a browser SharedWorker or server-side heartbeat aggregation per UserId.**Browser Throttling**Domain connection limits (6-10)Educate users or use different subdomains for distinct services.

6.5 System & Memory Management
------------------------------

*   **Message Formatting**: To reduce bandwidth and memory pressure, consider using compact binary formats like **MessagePack** instead of JSON.
    
*   **Garbage Collection (GC)**: Since the app runs in a container (e.g., M2 Mac with 32GB RAM), enable **Server GC**. This utilizes multiple heaps and threads, which is significantly more efficient for high-connection workloads than Workstation GC.
    
*   **Rate Limiting**: Implement the built-in ASP.NET Core RateLimiting middleware to prevent single users from monopolizing server resources.
    
*   **TCP Backlog**: Optimize the TCP backlog settings to ensure that spikes in concurrent connection requests (e.g., many users refreshing at once) are not dropped by the OS before being accepted by Kestrel.
using System.Net.Http.Json;
using Attic.Contracts.Admin;
using Attic.Contracts.Channels;
using Shouldly;
using Xunit;

namespace Attic.Api.IntegrationTests;

[Collection(nameof(AppHostCollection))]
public sealed class AuditLogFlowTests(AppHostFixture fx)
{
    [Fact]
    public async Task Delete_channel_writes_audit_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        var (client, _, _, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);

        var create = await client.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"ad-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        (await client.DeleteAsync($"/api/channels/{channel.Id:D}", ct)).EnsureSuccessStatusCode();

        var audit = (await (await client.GetAsync("/api/admin/audit/mine", ct))
            .Content.ReadFromJsonAsync<List<AuditLogEntryDto>>(ct))!;
        audit.ShouldContain(e => e.Action == "channel.delete" && e.TargetChannelId == channel.Id);
    }

    [Fact]
    public async Task Ban_member_writes_audit_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        var (owner, _, _, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);
        var create = await owner.PostAsJsonAsync("/api/channels",
            new CreateChannelRequest($"ad-{Guid.NewGuid():N}"[..20], null, "public"), ct);
        var channel = (await create.Content.ReadFromJsonAsync<ChannelDetails>(ct))!;

        var (joiner, joinerUsername, _, _) = await TestHelpers.RegisterFreshWithHandler(fx, ct);
        (await joiner.PostAsync($"/api/channels/{channel.Id:D}/join", null, ct)).EnsureSuccessStatusCode();

        var members = (await (await owner.GetAsync($"/api/channels/{channel.Id:D}/members", ct))
            .Content.ReadFromJsonAsync<List<ChannelMemberSummary>>(ct))!;
        var joinerId = members.First(m => m.Username == joinerUsername).UserId;

        (await owner.DeleteAsync($"/api/channels/{channel.Id:D}/members/{joinerId:D}", ct))
            .EnsureSuccessStatusCode();

        var audit = (await (await owner.GetAsync("/api/admin/audit/mine", ct))
            .Content.ReadFromJsonAsync<List<AuditLogEntryDto>>(ct))!;
        audit.ShouldContain(e => e.Action == "channel.ban_member"
            && e.TargetChannelId == channel.Id
            && e.TargetUserId == joinerId);
    }
}

using Attic.Domain.Entities;
using Shouldly;
using Xunit;

namespace Attic.Domain.Tests;

public class AttachmentTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-04-21T10:00:00Z");

    [Fact]
    public void Register_creates_unbound_attachment()
    {
        var a = Attachment.Register(
            id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            uploaderId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            originalFileName: "photo.jpg",
            contentType: "image/jpeg",
            sizeBytes: 12345,
            storagePath: "2026/04/21/abcd.bin",
            comment: null,
            now: T0);

        a.Id.ShouldBe(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        a.MessageId.ShouldBeNull();
        a.UploaderId.ShouldBe(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        a.OriginalFileName.ShouldBe("photo.jpg");
        a.ContentType.ShouldBe("image/jpeg");
        a.SizeBytes.ShouldBe(12345);
        a.StoragePath.ShouldBe("2026/04/21/abcd.bin");
        a.CreatedAt.ShouldBe(T0);
    }

    [Fact]
    public void Register_rejects_empty_storage_path()
    {
        Should.Throw<ArgumentException>(() => Attachment.Register(
            Guid.NewGuid(), Guid.NewGuid(), "a.bin", "application/octet-stream",
            1, "", null, T0)).ParamName.ShouldBe("storagePath");
    }

    [Fact]
    public void Register_rejects_non_positive_size()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Attachment.Register(
            Guid.NewGuid(), Guid.NewGuid(), "a.bin", "application/octet-stream",
            0, "x", null, T0)).ParamName.ShouldBe("sizeBytes");
    }

    [Fact]
    public void Register_trims_comment_and_nulls_empty()
    {
        var a = Attachment.Register(Guid.NewGuid(), Guid.NewGuid(), "a.bin",
            "application/octet-stream", 10, "x", comment: "  ", now: T0);
        a.Comment.ShouldBeNull();
    }

    [Fact]
    public void BindToMessage_sets_message_id()
    {
        var a = Attachment.Register(Guid.NewGuid(), Guid.NewGuid(), "a.bin",
            "application/octet-stream", 10, "x", null, T0);
        var messageId = 42L;
        a.BindToMessage(messageId);
        a.MessageId.ShouldBe(messageId);
    }

    [Fact]
    public void BindToMessage_rejects_rebinding()
    {
        var a = Attachment.Register(Guid.NewGuid(), Guid.NewGuid(), "a.bin",
            "application/octet-stream", 10, "x", null, T0);
        a.BindToMessage(42);
        Should.Throw<InvalidOperationException>(() => a.BindToMessage(43));
    }
}

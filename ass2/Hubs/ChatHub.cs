using BLL.Models;
using BLL.Services;
using Microsoft.AspNetCore.SignalR;

namespace ass2.Hubs;

public sealed class ChatHub(
    KnowledgeBaseService knowledgeBase,
    InternalSignalRToken internalToken) : Hub
{
    public async Task SendQuestion(string question)
    {
        if (!IsAllowedClient())
        {
            throw new HubException("Ban can dang nhap de su dung chat realtime.");
        }

        question = question.Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            await Clients.Caller.SendAsync("ReceiveChatError", "Hay nhap cau hoi truoc khi gui.");
            return;
        }

        var sessionId = GetSessionId();
        await Clients.Caller.SendAsync("ReceiveUserQuestion", question);

        try
        {
            var turn = await knowledgeBase.AskAsync(sessionId, question, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("ReceiveBotAnswer", ToPayload(turn));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await Clients.Caller.SendAsync("ReceiveChatError", "Khong tao duoc cau tra loi luc nay. Hay thu lai sau.");
        }
    }

    private string GetSessionId()
    {
        var httpContext = Context.GetHttpContext();
        return httpContext?.Request.Cookies["chat-session-id"] ?? Context.ConnectionId;
    }

    private bool IsAllowedClient()
    {
        if (Context.User?.Identity?.IsAuthenticated == true)
        {
            return true;
        }

        var httpContext = Context.GetHttpContext();
        return string.Equals(
            httpContext?.Request.Headers[InternalSignalRToken.HeaderName],
            internalToken.Value,
            StringComparison.Ordinal);
    }

    private static ChatTurnPayload ToPayload(ChatTurn turn)
    {
        return new ChatTurnPayload(
            turn.Question,
            turn.Answer,
            turn.Sources.Select(source => new SourcePayload(
                source.DocumentId,
                source.Title,
                source.Subject,
                source.Chapter,
                source.Teacher,
                source.Snippet,
                source.Score,
                source.FileName,
                source.HasOriginalFile,
                $"/Chatbot?handler=DownloadDocument&id={source.DocumentId}")).ToList(),
            turn.CreatedAt);
    }
}

public sealed record ChatTurnPayload(
    string Question,
    string Answer,
    IReadOnlyList<SourcePayload> Sources,
    DateTimeOffset CreatedAt);

public sealed record SourcePayload(
    Guid DocumentId,
    string Title,
    string Subject,
    string Chapter,
    string Teacher,
    string Snippet,
    int Score,
    string FileName,
    bool HasOriginalFile,
    string DownloadUrl);

public sealed record DocumentUploadedPayload(
    Guid Id,
    string Title,
    string FileName,
    string Department,
    string Subject,
    string Chapter,
    string Teacher,
    string UploadedBy,
    string Content,
    string UploadedAt,
    bool HasOriginalFile,
    string DownloadUrl);

public sealed record DocumentCreated(DocumentChangedPayload Document);

public sealed record DocumentUpdated(DocumentChangedPayload Document);

public sealed record DocumentReindexed(DocumentChangedPayload Document);

public sealed record DocumentDeleted(
    Guid Id,
    string Title,
    string Department,
    string Subject,
    string DeletedBy);

public sealed record DocumentChangedPayload(
    Guid Id,
    string Title,
    string FileName,
    string Department,
    string Subject,
    string Chapter,
    string Teacher,
    string UploadedBy,
    string UploadedByUserId,
    string Content,
    string UploadedAt,
    bool HasOriginalFile,
    string DownloadUrl,
    int ChunkCount);

public sealed class InternalSignalRToken
{
    public const string HeaderName = "X-Ass2-Internal-SignalR";
    public string Value { get; } = Guid.NewGuid().ToString("N");
}

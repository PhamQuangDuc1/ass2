using BLL.Services;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace ass2.Hubs;

public sealed class ChatHub(KnowledgeBaseService knowledgeBase) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var username = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(username))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(username));
        }

        await base.OnConnectedAsync();
    }

    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
    }

    public async Task Ask(string sessionId, string question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return;
        }

        var turn = await knowledgeBase.AskAsync(sessionId, question);
        await Clients.Group(sessionId).SendAsync("ReceiveAnswer", turn);
    }

    public static string UserGroup(string username) => $"user:{username.Trim().ToLowerInvariant()}";
}

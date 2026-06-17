using ass2.Services;
using Microsoft.AspNetCore.SignalR;

namespace ass2.Hubs;

public sealed class ChatHub(KnowledgeBaseService knowledgeBase) : Hub
{
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
}

using System;
using System.Collections.Generic;
using ToDoBot.Services.Bot;

namespace ToDoBot.DTO.Storage
{
    public class UserData
    {
        public int UserId { get; set; }
        public int ChatId { get; set; }

        public readonly Dictionary<EAction, object> PreData;
        public readonly Stack<EAction> Actions;
        public int? UtcOffset { get; set; }
        public int? Period { get; set; }
        public bool FirstMessageToday { get; set; }        
        public DateTime? LastMessageTo { get; set; }
        public DateTime? LastMessageFrom { get; set; }
        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        public UserData(int userid, int chatId)
        {
            UserId = userid;
            ChatId = chatId;

            PreData = new Dictionary<EAction, object>();
            Actions = new Stack<EAction>();
            FirstMessageToday = true;
            LastMessageTo = null;
            LastMessageFrom = null;
            UtcOffset = null;
            Period = null;
            From = null;
            To = null;
        }
    }
}
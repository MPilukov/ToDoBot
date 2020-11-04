using System;

namespace ToDoBot.DTO.Storage
{
    public class RecordData
    {
        public int UserId { get; set; }
        public Guid Id { get; set; }
        public string Data { get; set; }
        public DateTime Date { get; set; }
        public int Duration { get; set; }
    }
}
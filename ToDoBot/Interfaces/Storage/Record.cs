using System;

namespace ToDoBot.Interfaces.Storage
{
    public class Record
    {
        public Guid Id { get; set; }
        public string Data { get; set; }
        public DateTime Date { get; set; }
        public bool IsArchive { get; set; }
        public DateTime? ArchiveDate { get; set; }
        public int Duration { get; set; }
    }
}
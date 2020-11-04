using ToDoBot.DTO.Storage;
using System.Collections.Generic;
using System.Threading.Tasks;
using ToDoBot.Services.Bot;
using System;

namespace ToDoBot.Interfaces.Storage
{
    public interface IInfoStorage
    {
        Task AddRecord(RecordData recordData);
        Task UpdateRecord(int userId, Guid id, string text);
        Task<List<RecordData>> GetNewRecords(int userId);
        Task<List<RecordData>> GetArchiveRecords(int userId, DateTime archiveDate);
        Task ArchiveRecords(int userId, DateTime archiveDate);
        Task UpdateUser(UserData userData);
        Task<UserData> GetUser(int userId);
        Task<List<int>> GetUserIds();
    }
}
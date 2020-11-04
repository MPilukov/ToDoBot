using System.Collections.Generic;

namespace ToDoBot.Interfaces.Storage
{
    public class CommonData
    {
        public List<int> UserIds { get; set; }

        public CommonData()
        {
            UserIds = new List<int>();
        }
    }
}
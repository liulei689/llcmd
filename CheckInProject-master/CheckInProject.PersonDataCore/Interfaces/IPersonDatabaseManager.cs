using CheckInProject.PersonDataCore.Models;

namespace CheckInProject.PersonDataCore.Interfaces
{
    public interface IPersonDatabaseManager
    {
        public StringPersonDataBaseContext DatabaseService { get; }
        public IList<StringPersonDataBase> GetFaceData();
        public Task ImportFaceData(IList<StringPersonDataBase> faceData);
        public Task ClearFaceData();
        public Task AddFaceData(StringPersonDataBase faceData);
        public Task UpdateFaceData(StringPersonDataBase faceData);
        public Task DeleteFaceData(Guid studentId);
        public StringPersonDataBase? GetFaceDataById(Guid studentId);
    }
}

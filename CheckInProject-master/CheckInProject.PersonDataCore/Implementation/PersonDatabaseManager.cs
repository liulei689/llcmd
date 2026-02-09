using CheckInProject.PersonDataCore.Interfaces;
using CheckInProject.PersonDataCore.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CheckInProject.PersonDataCore.Implementation
{
    public class PersonDatabaseManager : IPersonDatabaseManager
    {
        public StringPersonDataBaseContext DatabaseService => Provider.GetRequiredService<StringPersonDataBaseContext>();
        private readonly IServiceProvider Provider;
        public IList<StringPersonDataBase> GetFaceData()
        {
            var result = DatabaseService.PersonData.OrderBy(t => t.ClassID).ToList();
            return result;
        }

        public async Task ImportFaceData(IList<StringPersonDataBase> faceData)
        {
            await ClearFaceData();
            DatabaseService.AddRange(faceData);
            await DatabaseService.SaveChangesAsync();
        }

        public async Task ClearFaceData()
        {
            var currentFaceData = GetFaceData();
            if (currentFaceData.Count != 0)
            {
                DatabaseService.RemoveRange(currentFaceData);
                await DatabaseService.SaveChangesAsync();
            }
        }

        public async Task AddFaceData(StringPersonDataBase faceData)
        {
            faceData.StudentID = Guid.NewGuid();
            DatabaseService.Add(faceData);
            await DatabaseService.SaveChangesAsync();
        }

        public async Task UpdateFaceData(StringPersonDataBase faceData)
        {
            var existing = await DatabaseService.PersonData.FindAsync(faceData.StudentID);
            if (existing != null)
            {
                existing.Name = faceData.Name;
                existing.ClassID = faceData.ClassID;
                existing.FaceEncodingString = faceData.FaceEncodingString;
                await DatabaseService.SaveChangesAsync();
            }
        }

        public async Task DeleteFaceData(Guid studentId)
        {
            var existing = await DatabaseService.PersonData.FindAsync(studentId);
            if (existing != null)
            {
                DatabaseService.Remove(existing);
                await DatabaseService.SaveChangesAsync();
            }
        }

        public StringPersonDataBase? GetFaceDataById(Guid studentId)
        {
            return DatabaseService.PersonData.FirstOrDefault(t => t.StudentID == studentId);
        }

        public PersonDatabaseManager(IServiceProvider provider)
        {
            Provider = provider;
        }
    }

}

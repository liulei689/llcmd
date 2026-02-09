using CheckInProject.CheckInCore.Models;
using CheckInProject.PersonDataCore.Models;

namespace CheckInProject.CheckInCore.Interfaces
{
    public interface ICheckInManager
    {
        public Task CheckIn(DateOnly currentDate, TimeOnly currentTime, Guid studentId);
        public List<CheckInDataExportModels> QueryTodayRecords();
        public Task<List<CheckInDataModels>> GetTodayCheckInData();
        public List<StringPersonDataBase> QueryRequestedTimeUncheckedRecords(TimeEnum? targetTime);
        public Task ExportRecordsToExcelFile(ExportTypeEnum exportType, string path, TimeEnum? targetTime = null);
        public Task ClearCheckInRecords();
    }
}

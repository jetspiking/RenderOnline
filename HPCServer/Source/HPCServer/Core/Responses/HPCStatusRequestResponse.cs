using HPCServer.Core.Configuration;

namespace HPCServer.Core.Responses
{
    public class HPCStatusRequestResponse
    {
        public Boolean IsAvailable { get; set; } = true;
        public UInt64? LastTaskId { get; set; } = null;
        public Boolean IsLastSuccessfull { get; set; } = true;

        public HPCStatusRequestResponse(Boolean isAvailable, UInt64? lastTaskId, Boolean isLastSuccessfull)
        {
            IsAvailable = isAvailable;
            LastTaskId = lastTaskId;
            IsLastSuccessfull = isLastSuccessfull;
        }
    }
}

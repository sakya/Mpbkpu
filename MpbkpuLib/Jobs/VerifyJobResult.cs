using MpbkpuLib.Abstracts;

namespace MpbkpuLib.Jobs;

public class VerifyJobResult : BaseJobResult
{
    #region classes
    public class Error
    {
        public enum ErrorCodes
        {
            BackupNotFound,
            BackupDirectoryNotFound,
            FileNotFound,
            Md5Mismatch
        }

        public Error(ErrorCodes code, string message)
        {
            Code = code;
            Message = message;
        }

        public ErrorCodes Code { get; set; }
        public string Message { get; set; }
    }
    #endregion

    public List<Error>? Errors { get; set; }
}
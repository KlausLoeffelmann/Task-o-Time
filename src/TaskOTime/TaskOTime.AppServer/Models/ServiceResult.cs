namespace TaskOTime.AppServer.Models
{
    public class ServiceResult
    {
        protected ServiceResult(bool success, string errorCode, string errorMessage)
        {
            Success = success;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }

        public string ErrorCode { get; }

        public string ErrorMessage { get; }

        public static ServiceResult Ok()
        {
            return new ServiceResult(true, null, null);
        }

        public static ServiceResult Fail(string errorCode, string errorMessage)
        {
            return new ServiceResult(false, errorCode, errorMessage);
        }
    }

    public sealed class ServiceResult<T> : ServiceResult
    {
        private ServiceResult(bool success, T value, string errorCode, string errorMessage)
            : base(success, errorCode, errorMessage)
        {
            Value = value;
        }

        public T Value { get; }

        public static ServiceResult<T> Ok(T value)
        {
            return new ServiceResult<T>(true, value, null, null);
        }

        public new static ServiceResult<T> Fail(string errorCode, string errorMessage)
        {
            return new ServiceResult<T>(false, default(T), errorCode, errorMessage);
        }
    }
}

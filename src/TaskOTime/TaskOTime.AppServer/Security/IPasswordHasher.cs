namespace TaskOTime.AppServer.Security
{
    public interface IPasswordHasher
    {
        PasswordHashResult CreateHash(string password);

        bool VerifyHash(string password, string passwordHash, string passwordSalt);
    }
}

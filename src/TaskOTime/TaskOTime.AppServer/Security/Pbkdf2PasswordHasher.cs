using System;
using System.Security.Cryptography;

namespace TaskOTime.AppServer.Security
{
    public sealed class Pbkdf2PasswordHasher : IPasswordHasher
    {
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int Iterations = 10000;

        public PasswordHashResult CreateHash(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                throw new ArgumentException("A password is required.", nameof(password));
            }

            var saltBytes = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(saltBytes);
            }

            var salt = Convert.ToBase64String(saltBytes);
            return new PasswordHashResult(HashPassword(password, salt), salt);
        }

        public bool VerifyHash(string password, string passwordHash, string passwordSalt)
        {
            if (string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(passwordHash) ||
                string.IsNullOrWhiteSpace(passwordSalt))
            {
                return false;
            }

            try
            {
                var computedHash = HashPassword(password, passwordSalt);
                return FixedTimeEquals(
                    Convert.FromBase64String(computedHash),
                    Convert.FromBase64String(passwordHash));
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static string HashPassword(string password, string salt)
        {
            var saltBytes = Convert.FromBase64String(salt);
            using (var deriveBytes = new Rfc2898DeriveBytes(password, saltBytes, Iterations))
            {
                return Convert.ToBase64String(deriveBytes.GetBytes(HashSize));
            }
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            var diff = 0;
            for (var i = 0; i < left.Length; i++)
            {
                diff |= left[i] ^ right[i];
            }

            return diff == 0;
        }
    }
}

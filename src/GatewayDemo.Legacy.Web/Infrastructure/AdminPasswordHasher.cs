using System;
using System.Security.Cryptography;
using System.Text;

namespace GatewayDemo.Legacy.Web.Infrastructure
{
    public sealed class AdminPasswordHasher
    {
        private const string Prefix = "pbkdf2-sha256";

        public bool VerifyHashedPassword(string hashedPassword, string providedPassword)
        {
            if (string.IsNullOrWhiteSpace(hashedPassword) || string.IsNullOrWhiteSpace(providedPassword))
            {
                return false;
            }

            var parts = hashedPassword.Split(new[] { '$' }, StringSplitOptions.RemoveEmptyEntries);
            int iterations;
            if (parts.Length != 4
                || !string.Equals(parts[0], Prefix, StringComparison.Ordinal)
                || !int.TryParse(parts[1], out iterations)
                || iterations <= 0)
            {
                return false;
            }

            byte[] salt;
            byte[] expectedHash;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expectedHash = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            byte[] actualHash;
            using (var deriveBytes = new Rfc2898DeriveBytes(
                Encoding.UTF8.GetBytes(providedPassword),
                salt,
                iterations,
                HashAlgorithmName.SHA256))
            {
                actualHash = deriveBytes.GetBytes(expectedHash.Length);
            }

            return FixedTimeEquals(actualHash, expectedHash);
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            var diff = 0;
            for (var index = 0; index < left.Length; index++)
            {
                diff |= left[index] ^ right[index];
            }

            return diff == 0;
        }
    }
}

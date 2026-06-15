using Microsoft.VisualStudio.TestTools.UnitTesting;
using TaskOTime.AppServer.Models;
using TaskOTime.AppServer.Security;

namespace TaskOTime.AppServer.Tests
{
    [TestClass]
    public class SecurityAndServiceResultTests
    {
        [TestMethod]
        public void Pbkdf2PasswordHasher_VerifiesCreatedHash()
        {
            var hasher = new Pbkdf2PasswordHasher();

            var result = hasher.CreateHash("Correct Horse Battery Staple");

            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Hash));
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Salt));
            Assert.IsTrue(hasher.VerifyHash("Correct Horse Battery Staple", result.Hash, result.Salt));
        }

        [TestMethod]
        public void Pbkdf2PasswordHasher_FailsForWrongPasswordOrInvalidHashInputs()
        {
            var hasher = new Pbkdf2PasswordHasher();
            var result = hasher.CreateHash("expected password");

            Assert.IsFalse(hasher.VerifyHash("wrong password", result.Hash, result.Salt));
            Assert.IsFalse(hasher.VerifyHash("expected password", "not-base64", result.Salt));
            Assert.IsFalse(hasher.VerifyHash("expected password", result.Hash, "not-base64"));
            Assert.IsFalse(hasher.VerifyHash(null, result.Hash, result.Salt));
            Assert.IsFalse(hasher.VerifyHash("expected password", null, result.Salt));
            Assert.IsFalse(hasher.VerifyHash("expected password", result.Hash, null));
        }

        [TestMethod]
        public void ServiceResult_OkAndFail_ExposeExpectedState()
        {
            var ok = ServiceResult.Ok();
            var fail = ServiceResult.Fail("ValidationError", "The request is invalid.");

            Assert.IsTrue(ok.Success);
            Assert.IsNull(ok.ErrorCode);
            Assert.IsNull(ok.ErrorMessage);
            Assert.IsFalse(fail.Success);
            Assert.AreEqual("ValidationError", fail.ErrorCode);
            Assert.AreEqual("The request is invalid.", fail.ErrorMessage);
        }

        [TestMethod]
        public void GenericServiceResult_OkAndFail_ExposeExpectedStateAndValue()
        {
            var ok = ServiceResult<string>.Ok("created");
            var fail = ServiceResult<string>.Fail("NotFound", "The item was not found.");

            Assert.IsTrue(ok.Success);
            Assert.AreEqual("created", ok.Value);
            Assert.IsNull(ok.ErrorCode);
            Assert.IsNull(ok.ErrorMessage);
            Assert.IsFalse(fail.Success);
            Assert.IsNull(fail.Value);
            Assert.AreEqual("NotFound", fail.ErrorCode);
            Assert.AreEqual("The item was not found.", fail.ErrorMessage);
        }
    }
}

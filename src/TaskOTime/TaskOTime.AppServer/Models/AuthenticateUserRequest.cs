using System;

namespace TaskOTime.AppServer.Models
{
    public sealed class AuthenticateUserRequest
    {
        public Guid? IdTenant { get; set; }

        public string UserIdentOrEmail { get; set; }

        public string Password { get; set; }
    }
}

using System;

namespace TaskOTime.AppServer.Models
{
    public sealed class CreateUserRequest
    {
        public Guid IdTenant { get; set; }

        public string UserIdent { get; set; }

        public string FirstName { get; set; }

        public string MiddleName { get; set; }

        public string LastName { get; set; }

        public string EMail { get; set; }

        public bool IsAdmin { get; set; }

        public string TemporaryPassword { get; set; }

        public DateTimeOffset? PreliminaryPasswordExpiresAt { get; set; }

        public int? EmojiIndex { get; set; }

        public int? MaxProjects { get; set; }
    }
}

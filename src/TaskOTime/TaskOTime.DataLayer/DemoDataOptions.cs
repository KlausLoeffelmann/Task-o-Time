using System;
using System.Collections.Generic;
using System.Linq;

namespace TaskOTime.DataLayer
{
    public sealed class DemoDataOptions
    {
        public int Tenants { get; set; } = 1;
        public int Projects { get; set; } = 5;
        public List<DemoUserOptions> Users { get; set; } = new List<DemoUserOptions>
        {
            new DemoUserOptions()
        };
        public int Categories { get; set; } = 20;
        public int Lists { get; set; } = 4;
        public DemoTaskOptions Tasks { get; set; } = new DemoTaskOptions();
        public int TimeItems { get; set; } = 100;

        public void Validate()
        {
            RequireRange(Tenants, 1, 10, nameof(Tenants));
            RequireRange(Projects, 2, 20, nameof(Projects));
            RequireRange(Categories, 10, 50, nameof(Categories));
            RequireRange(Lists, 2, 10, nameof(Lists));
            RequireRange(TimeItems, 50, 1000, nameof(TimeItems));

            if (Tasks == null)
            {
                throw new InvalidOperationException("Task settings are required.");
            }

            Tasks.Validate();

            if (Users == null || Users.Count == 0)
            {
                throw new InvalidOperationException("At least one demo user is required.");
            }

            foreach (var user in Users)
            {
                user.Validate();
            }

            var duplicateHandles = Users.GroupBy(u => u.Handle, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();
            if (duplicateHandles.Length > 0)
            {
                throw new InvalidOperationException("Duplicate demo user handles are not allowed: " + string.Join(", ", duplicateHandles));
            }
        }

        private static void RequireRange(int value, int min, int max, string name)
        {
            if (value < min || value > max)
            {
                throw new InvalidOperationException(name + " must be between " + min + " and " + max + ".");
            }
        }
    }

    public sealed class DemoTaskOptions
    {
        public int Open { get; set; } = 15;
        public int Closed { get; set; } = 20;

        public int Total => Open + Closed;

        public void Validate()
        {
            if (Open < 0 || Closed < 0)
            {
                throw new InvalidOperationException("Open and closed task counts cannot be negative.");
            }

            if (Total < 30 || Total > 200)
            {
                throw new InvalidOperationException("The total task count must be between 30 and 200.");
            }
        }
    }

    public sealed class DemoUserOptions
    {
        public string Handle { get; set; } = "Admin";
        public string Password { get; set; } = "P@$$w0rd";
        public string FirstName { get; set; } = "John";
        public string LastName { get; set; } = "Doe";
        public string Email { get; set; } = "john@doe.de";

        public void Validate()
        {
            RequireValue(Handle, nameof(Handle));
            RequireValue(Password, nameof(Password));
            RequireValue(FirstName, nameof(FirstName));
            RequireValue(LastName, nameof(LastName));
            RequireValue(Email, nameof(Email));
        }

        private static void RequireValue(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Demo user " + name + " is required.");
            }
        }
    }
}

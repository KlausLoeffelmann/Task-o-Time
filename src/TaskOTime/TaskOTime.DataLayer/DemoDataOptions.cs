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
        public DemoProjectAssignmentOptions ProjectAssignments { get; set; } = new DemoProjectAssignmentOptions();
        public DemoTextOptions Text { get; set; } = new DemoTextOptions();

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

            if (ProjectAssignments == null)
            {
                throw new InvalidOperationException("Project assignment settings are required.");
            }

            ProjectAssignments.Validate(Projects, Users ?? new List<DemoUserOptions>());

            if (Text == null)
            {
                throw new InvalidOperationException("Demo text settings are required.");
            }

            Text.Validate();

            if (Users == null || Users.Count == 0)
            {
                throw new InvalidOperationException("At least one demo user is required.");
            }

            foreach (var user in Users)
            {
                user.Validate(Tenants);
            }

            if (!Users.Any(u => u.IsAdmin))
            {
                throw new InvalidOperationException("At least one demo user must be configured as an admin.");
            }

            var duplicateHandles = Users.GroupBy(u => u.Handle, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();
            if (duplicateHandles.Length > 0)
            {
                throw new InvalidOperationException("Duplicate demo user handles are not allowed: " + string.Join(", ", duplicateHandles));
            }

            var missingTenantNumbers = Enumerable.Range(1, Tenants)
                .Where(tenantNumber => !Users.Select((u, index) => u.TenantIndex ?? ((index % Tenants) + 1)).Contains(tenantNumber))
                .ToArray();
            if (missingTenantNumbers.Length > 0)
            {
                throw new InvalidOperationException("At least one demo user must be assigned to each tenant. Missing tenant(s): " + string.Join(", ", missingTenantNumbers));
            }

            ProjectAssignments.ValidateTenantUserOverrides(Tenants, Users);
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
        public string FirstName { get; set; } = "Anna";
        public string LastName { get; set; } = "Schneider";
        public string Email { get; set; } = "anna.schneider@example.invalid";
        public bool IsAdmin { get; set; } = true;
        public int? TenantIndex { get; set; }

        public void Validate(int tenantCount)
        {
            RequireValue(Handle, nameof(Handle));
            RequireValue(Password, nameof(Password));
            RequireValue(FirstName, nameof(FirstName));
            RequireValue(LastName, nameof(LastName));
            RequireValue(Email, nameof(Email));
            if (TenantIndex.HasValue && (TenantIndex.Value < 1 || TenantIndex.Value > tenantCount))
            {
                throw new InvalidOperationException("Demo user TenantIndex must be between 1 and " + tenantCount + ".");
            }
        }

        private static void RequireValue(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Demo user " + name + " is required.");
            }
        }
    }

    public sealed class DemoProjectAssignmentOptions
    {
        public string Mode { get; set; } = "AllTenantUsers";
        public int UsersPerProject { get; set; } = 2;
        public int Seed { get; set; } = 20240614;
        public bool IncludeAdmins { get; set; } = true;
        public List<DemoProjectAssignmentOverride> Projects { get; set; } = new List<DemoProjectAssignmentOverride>();

        public void Validate(int projectCount, IReadOnlyList<DemoUserOptions> users)
        {
            if (!IsAllTenantUsers && !IsDeterministic && !IsRandom && !IsPerProject)
            {
                throw new InvalidOperationException("Project assignment mode must be AllTenantUsers, Deterministic, Random, or PerProject.");
            }

            if (UsersPerProject < 1 || UsersPerProject > 50)
            {
                throw new InvalidOperationException("Project assignment UsersPerProject must be between 1 and 50.");
            }

            if (Projects == null)
            {
                Projects = new List<DemoProjectAssignmentOverride>();
            }

            var duplicateProjectNumbers = Projects.GroupBy(p => p.ProjectNumber)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToArray();
            if (duplicateProjectNumbers.Length > 0)
            {
                throw new InvalidOperationException("Duplicate project assignment overrides are not allowed: " + string.Join(", ", duplicateProjectNumbers));
            }

            var knownHandles = new HashSet<string>(users.Select(u => u.Handle), StringComparer.OrdinalIgnoreCase);
            foreach (var project in Projects)
            {
                project.Validate(projectCount, knownHandles);
            }
        }

        public void ValidateTenantUserOverrides(int tenantCount, IReadOnlyList<DemoUserOptions> users)
        {
            if (Projects == null || Projects.Count == 0)
            {
                return;
            }

            var userTenantNumbers = users.Select((u, index) => new
                {
                    u.Handle,
                    TenantNumber = u.TenantIndex ?? ((index % tenantCount) + 1)
                })
                .ToDictionary(u => u.Handle, u => u.TenantNumber, StringComparer.OrdinalIgnoreCase);

            foreach (var project in Projects)
            {
                var projectTenantNumber = ((project.ProjectNumber - 1) % tenantCount) + 1;
                var crossTenantUsers = project.Users
                    .Where(handle => userTenantNumbers.TryGetValue(handle, out var tenantNumber) && tenantNumber != projectTenantNumber)
                    .ToArray();
                if (crossTenantUsers.Length > 0)
                {
                    throw new InvalidOperationException("Project " + project.ProjectNumber + " assignment contains user(s) from another tenant: " + string.Join(", ", crossTenantUsers));
                }
            }
        }

        public bool IsAllTenantUsers => string.Equals(Mode, "AllTenantUsers", StringComparison.OrdinalIgnoreCase);
        public bool IsDeterministic => string.Equals(Mode, "Deterministic", StringComparison.OrdinalIgnoreCase);
        public bool IsRandom => string.Equals(Mode, "Random", StringComparison.OrdinalIgnoreCase);
        public bool IsPerProject => string.Equals(Mode, "PerProject", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class DemoProjectAssignmentOverride
    {
        public int ProjectNumber { get; set; }
        public List<string> Users { get; set; } = new List<string>();

        public void Validate(int projectCount, ISet<string> knownHandles)
        {
            if (ProjectNumber < 1 || ProjectNumber > projectCount)
            {
                throw new InvalidOperationException("Project assignment ProjectNumber must be between 1 and " + projectCount + ".");
            }

            if (Users == null || Users.Count == 0)
            {
                throw new InvalidOperationException("Project assignment Users must contain at least one user handle.");
            }

            var unknownHandles = Users.Where(handle => !knownHandles.Contains(handle)).ToArray();
            if (unknownHandles.Length > 0)
            {
                throw new InvalidOperationException("Project assignment references unknown user handle(s): " + string.Join(", ", unknownHandles));
            }
        }
    }

    public sealed class DemoTextOptions
    {
        public int Seed { get; set; } = 20240614;
        public string Style { get; set; } = "Mixed";
        public int GermanWeight { get; set; } = 1;
        public int DutchWeight { get; set; } = 1;

        public void Validate()
        {
            if (!IsGerman && !IsDutch && !IsMixed)
            {
                throw new InvalidOperationException("Demo text style must be German, Dutch, or Mixed.");
            }

            if (GermanWeight < 0 || DutchWeight < 0)
            {
                throw new InvalidOperationException("Demo text language weights cannot be negative.");
            }

            if (IsMixed && GermanWeight + DutchWeight == 0)
            {
                throw new InvalidOperationException("Mixed demo text requires at least one positive language weight.");
            }
        }

        public bool IsGerman => string.Equals(Style, "German", StringComparison.OrdinalIgnoreCase);
        public bool IsDutch => string.Equals(Style, "Dutch", StringComparison.OrdinalIgnoreCase);
        public bool IsMixed => string.Equals(Style, "Mixed", StringComparison.OrdinalIgnoreCase);
    }
}
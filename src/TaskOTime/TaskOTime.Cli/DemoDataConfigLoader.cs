using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using TaskOTime.DataLayer;

namespace TaskOTime.Cli
{
    internal static class DemoDataConfigLoader
    {
        public static DemoDataOptions Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "demodataconfig.json");
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The demo data configuration file was not found.", path);
            }

            return LoadJson(File.ReadAllText(path));
        }

        internal static DemoDataOptions LoadJson(string json)
        {
            var serializer = new JavaScriptSerializer();
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null)
            {
                throw new InvalidOperationException("The demo data configuration must be a JSON object.");
            }

            var options = new DemoDataOptions
            {
                Tenants = GetInt(root, "tenants", 1),
                Projects = GetInt(root, "projects", 5),
                Categories = GetInt(root, "categories", 20),
                Lists = GetInt(root, "lists", 4),
                TimeItems = GetInt(root, "timeItems", 100),
                TimeItemDates = ReadTimeItemDates(root),
                Tasks = ReadTasks(root),
                Users = ReadUsers(root),
                ProjectAssignments = ReadProjectAssignments(root),
                Text = ReadText(root)
            };

            options.Validate();
            return options;
        }

        private static DemoTimeItemDateOptions ReadTimeItemDates(Dictionary<string, object> root)
        {
            var dates = GetObject(root, "timeItemDates");
            if (dates == null)
            {
                return new DemoTimeItemDateOptions();
            }

            return new DemoTimeItemDateOptions
            {
                Days = GetInt(dates, "days", 30),
                IncludeToday = GetBool(dates, "includeToday", true),
                SkipWeekends = GetBool(dates, "skipWeekends", false)
            };
        }

        private static DemoTaskOptions ReadTasks(Dictionary<string, object> root)
        {
            var tasks = GetObject(root, "tasks");
            if (tasks == null)
            {
                return new DemoTaskOptions();
            }

            return new DemoTaskOptions
            {
                Open = GetInt(tasks, "open", 15),
                Closed = GetInt(tasks, "closed", 20)
            };
        }

        private static List<DemoUserOptions> ReadUsers(Dictionary<string, object> root)
        {
            if (!root.TryGetValue("users", out var value) || value == null)
            {
                return new List<DemoUserOptions> { new DemoUserOptions() };
            }

            var userObjects = value as object[];
            if (userObjects == null)
            {
                throw new InvalidOperationException("The users setting must be a JSON array.");
            }

            var users = new List<DemoUserOptions>();
            foreach (var userObject in userObjects)
            {
                var user = userObject as Dictionary<string, object>;
                if (user == null)
                {
                    throw new InvalidOperationException("Each users entry must be a JSON object.");
                }

                users.Add(new DemoUserOptions
                {
                    Handle = GetString(user, "handle", "Admin"),
                    Password = GetString(user, "password", "P@$$w0rd"),
                    FirstName = GetString(user, "firstName", "Anna"),
                    LastName = GetString(user, "lastName", "Schneider"),
                    Email = GetString(user, "email", "anna.schneider@example.invalid"),
                    IsAdmin = GetBool(user, "isAdmin", users.Count == 0),
                    TenantIndex = GetNullableInt(user, "tenantIndex") ?? GetNullableInt(user, "tenant")
                });
            }

            return users;
        }

        private static DemoProjectAssignmentOptions ReadProjectAssignments(Dictionary<string, object> root)
        {
            var assignments = GetObject(root, "projectAssignments");
            if (assignments == null)
            {
                return new DemoProjectAssignmentOptions();
            }

            return new DemoProjectAssignmentOptions
            {
                Mode = GetString(assignments, "mode", "AllTenantUsers"),
                UsersPerProject = GetInt(assignments, "usersPerProject", 2),
                Seed = GetInt(assignments, "seed", 20240614),
                IncludeAdmins = GetBool(assignments, "includeAdmins", true),
                Projects = ReadProjectAssignmentOverrides(assignments)
            };
        }

        private static List<DemoProjectAssignmentOverride> ReadProjectAssignmentOverrides(Dictionary<string, object> assignments)
        {
            if (!assignments.TryGetValue("projects", out var value) || value == null)
            {
                return new List<DemoProjectAssignmentOverride>();
            }

            var projectObjects = value as object[];
            if (projectObjects == null)
            {
                throw new InvalidOperationException("The projectAssignments.projects setting must be a JSON array.");
            }

            var projects = new List<DemoProjectAssignmentOverride>();
            foreach (var projectObject in projectObjects)
            {
                var project = projectObject as Dictionary<string, object>;
                if (project == null)
                {
                    throw new InvalidOperationException("Each projectAssignments.projects entry must be a JSON object.");
                }

                projects.Add(new DemoProjectAssignmentOverride
                {
                    ProjectNumber = GetInt(project, "projectNumber", 0),
                    Users = ReadStringArray(project, "users")
                });
            }

            return projects;
        }

        private static DemoTextOptions ReadText(Dictionary<string, object> root)
        {
            var text = GetObject(root, "text");
            if (text == null)
            {
                return new DemoTextOptions();
            }

            return new DemoTextOptions
            {
                Seed = GetInt(text, "seed", 20240614),
                Style = GetString(text, "style", "Mixed"),
                GermanWeight = GetInt(text, "germanWeight", 1),
                DutchWeight = GetInt(text, "dutchWeight", 1)
            };
        }

        private static Dictionary<string, object> GetObject(Dictionary<string, object> values, string name)
        {
            if (!values.TryGetValue(name, out var value) || value == null)
            {
                return null;
            }

            var result = value as Dictionary<string, object>;
            if (result == null)
            {
                throw new InvalidOperationException("The " + name + " setting must be a JSON object.");
            }

            return result;
        }

        private static string GetString(Dictionary<string, object> values, string name, string defaultValue)
        {
            return values.TryGetValue(name, out var value) && value != null ? Convert.ToString(value) : defaultValue;
        }

        private static int GetInt(Dictionary<string, object> values, string name, int defaultValue)
        {
            if (!values.TryGetValue(name, out var value) || value == null)
            {
                return defaultValue;
            }

            return Convert.ToInt32(value);
        }

        private static int? GetNullableInt(Dictionary<string, object> values, string name)
        {
            if (!values.TryGetValue(name, out var value) || value == null)
            {
                return null;
            }

            return Convert.ToInt32(value);
        }

        private static bool GetBool(Dictionary<string, object> values, string name, bool defaultValue)
        {
            if (!values.TryGetValue(name, out var value) || value == null)
            {
                return defaultValue;
            }

            return Convert.ToBoolean(value);
        }

        private static List<string> ReadStringArray(Dictionary<string, object> values, string name)
        {
            if (!values.TryGetValue(name, out var value) || value == null)
            {
                return new List<string>();
            }

            var items = value as object[];
            if (items == null)
            {
                throw new InvalidOperationException("The " + name + " setting must be a JSON array.");
            }

            var strings = new List<string>();
            foreach (var item in items)
            {
                strings.Add(Convert.ToString(item));
            }

            return strings;
        }
    }
}

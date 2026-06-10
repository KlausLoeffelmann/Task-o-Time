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

            var serializer = new JavaScriptSerializer();
            var root = serializer.DeserializeObject(File.ReadAllText(path)) as Dictionary<string, object>;
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
                Tasks = ReadTasks(root),
                Users = ReadUsers(root)
            };

            options.Validate();
            return options;
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
                    FirstName = GetString(user, "firstName", "John"),
                    LastName = GetString(user, "lastName", "Doe"),
                    Email = GetString(user, "email", "john@doe.de")
                });
            }

            return users;
        }

        private static Dictionary<string, object> GetObject(Dictionary<string, object> values, string name)
        {
            return values.TryGetValue(name, out var value) ? value as Dictionary<string, object> : null;
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
    }
}

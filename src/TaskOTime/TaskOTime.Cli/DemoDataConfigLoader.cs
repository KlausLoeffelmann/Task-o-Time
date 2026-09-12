using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            var root = ReadJson(json) as Dictionary<string, object>;
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

        private static object ReadJson(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (json.Length > 2097152) throw new ArgumentException("The JSON configuration exceeds the maximum length.", nameof(json));
            if (string.IsNullOrWhiteSpace(json)) return null;
            RejectCommentsAndTrailingCommas(json);
            try
            {
                using (var reader = new ConfigurationJsonReader(json))
                {
                    var token = JToken.Load(reader);
                    if (reader.Read()) throw new ArgumentException("Unexpected content after the JSON value.", nameof(json));
                    return ReadJsonValue(token);
                }
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("Invalid JSON configuration.", nameof(json), exception);
            }
        }

        private static object ReadJsonValue(JToken token)
        {
            if (token is JObject values)
                return values.Properties().ToDictionary(p => p.Name, p => ReadJsonValue(p.Value), StringComparer.Ordinal);
            if (token is JArray array) return array.Select(ReadJsonValue).ToArray();
            if (token is JValue value && token.Type != JTokenType.Undefined) return value.Value;
            throw new ArgumentException("Unsupported JSON value.");
        }

        private static void RejectCommentsAndTrailingCommas(string json)
        {
            char quote = '\0';
            for (var index = 0; index < json.Length; index++)
            {
                var value = json[index];
                if (quote != '\0')
                {
                    if (value == '\\') index++;
                    else if (value == quote) quote = '\0';
                    continue;
                }
                if (value == '"' || value == '\'') quote = value;
                else if (value == '/' && index + 1 < json.Length && (json[index + 1] == '/' || json[index + 1] == '*'))
                    throw new ArgumentException("JSON comments are not supported.", nameof(json));
                else if (value == ',')
                {
                    var next = index + 1;
                    while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                    if (next < json.Length && (json[next] == '}' || json[next] == ']'))
                        throw new ArgumentException("Trailing JSON commas are not supported.", nameof(json));
                }
            }
        }

        private sealed class ConfigurationJsonReader : JsonTextReader
        {
            private readonly string json;
            private readonly List<int> lineStarts = new List<int> { 0 };

            public ConfigurationJsonReader(string json) : base(new StringReader(json))
            {
                this.json = json;
                DateParseHandling = DateParseHandling.None;
                MaxDepth = 100;
                for (var index = 0; index < json.Length; index++)
                {
                    if (json[index] == '\r')
                    {
                        if (index + 1 < json.Length && json[index + 1] == '\n') index++;
                        lineStarts.Add(index + 1);
                    }
                    else if (json[index] == '\n') lineStarts.Add(index + 1);
                }
            }

            public override bool Read()
            {
                if (!base.Read()) return false;
                if (TokenType == JsonToken.String && Value is string text)
                {
                    var date = Regex.Match(text, @"^/Date\((-?\d+)(?:[+-]\d{4})?\)/$");
                    var tokenEnd = lineStarts[LineNumber - 1] + LinePosition;
                    var rawLength = text.Length + 4;
                    if (date.Success && tokenEnd >= rawLength &&
                        json.Substring(tokenEnd - rawLength, rawLength) == "\"" + text.Replace("/", "\\/") + "\"")
                    {
                        var milliseconds = long.Parse(date.Groups[1].Value, CultureInfo.InvariantCulture);
                        SetToken(JsonToken.Date, new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                            .AddTicks(checked(milliseconds * TimeSpan.TicksPerMillisecond)));
                    }
                }
                if (TokenType != JsonToken.Integer && TokenType != JsonToken.Float) return true;
                // Retain the legacy Int32/Int64/Decimal/Double ladder and exponent handling.
                var end = lineStarts[LineNumber - 1] + LinePosition;
                var start = end;
                while (start > 0 && !char.IsWhiteSpace(json[start - 1]) && "[{,:".IndexOf(json[start - 1]) < 0) start--;
                var number = json.Substring(start, end - start);
                if (int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                    SetToken(JsonToken.Integer, integer);
                else if (long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longInteger))
                    SetToken(JsonToken.Integer, longInteger);
                else if (decimal.TryParse(number, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalNumber))
                    SetToken(JsonToken.Float, decimalNumber);
                else if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleNumber) &&
                         (!double.IsInfinity(doubleNumber) || number == "Infinity" || number == "-Infinity"))
                    SetToken(JsonToken.Float, doubleNumber);
                else throw new ArgumentException("Invalid JSON number.");
                return true;
            }
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

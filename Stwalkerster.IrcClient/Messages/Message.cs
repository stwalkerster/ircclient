namespace Stwalkerster.IrcClient.Messages
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;

    /// <summary>
    /// The message.
    /// </summary>
    public class Message : IMessage
    {
        private readonly IEnumerable<string> parameters;

        private readonly IDictionary<string, string> tags;

        public Message(string command)
            : this(null, command, null)
        {
        }
        
        public Message(string command, string parameter)
            : this(null, command, new List<string> {parameter})
        {
        }
        
        public Message(string command, IEnumerable<string> parameters)
            : this(null, command, parameters)
        {
        }
        
        public Message(string prefix, string command, IEnumerable<string> parameters)
            :this(prefix, command, parameters, null)
        {
        }
        
        public Message(string prefix, string command, IEnumerable<string> parameters, IDictionary<string, string> tags)
        {
            this.Prefix = prefix;
            this.Command = command;
            this.parameters = parameters ?? new List<string>();
            this.tags = tags ?? new Dictionary<string, string>();
        }

        public string Command { get; }

        public string Prefix { get; }

        public IEnumerable<string> Parameters => this.parameters?.ToArray();

        public IDictionary<string, string> Tags => this.tags == null ? null : new ReadOnlyDictionary<string, string>(this.tags);

        public static IMessage Parse(string data)
        {
            var separator = new[] {' '};
            
            // Define the parts of the message
            // It's always going to be an optional prefix (prefixed with a :), a command word, and 0 or more parameters to the command
            
            string prefix = null;
            string command;
            List<string> messageParameters = null;
            Dictionary<string, string> tags = null;

            if (data.StartsWith("@"))
            {
                // Split the incoming data into the tag string and remainder
                var tagsplit = data.Split(separator, 2, StringSplitOptions.RemoveEmptyEntries);
                
                // overwrite the original data, so we don't have to think about the tags later.
                // This is now a standard 1459 message.
                data = tagsplit[1];

                var rawtags = tagsplit[0];

                tags = rawtags.Substring(1).Split(';')
                    .ToDictionary(
                        keySelector: x => x.Contains("=") ? x.Substring(0, x.IndexOf("=", StringComparison.Ordinal)) : x,
                        elementSelector: x =>
                        {
                            if (!x.Contains("="))
                            {
                                return string.Empty;
                            }

                            var escapedValue = x.Substring(x.IndexOf("=", StringComparison.Ordinal) + 1);

                            return escapedValue
                                .Replace("\\s", " ")
                                .Replace("\\r", "\r")
                                .Replace("\\n", "\n")
                                .Replace("\\:", ";")
                                .Replace("\\\\", "\\");
                        });
            }
            
            // Look for a prefix
            if (data.StartsWith(":"))
            {
                // Split the incoming data into a prefix and remainder
                var prefixstrings = data.Split(separator, 2, StringSplitOptions.RemoveEmptyEntries);
                
                // overwrite the original data, so we don't have to think about the prefix later.
                // This is now a command word, and 0 or more parameters to the command.
                data = prefixstrings[1];
                
                // Extract the prefix itself, stripping the leading : too.
                prefix = prefixstrings[0].Substring(1);
            }

            // Split out the command word
            var strings = data.Split(separator, 2, StringSplitOptions.RemoveEmptyEntries);
            command = strings[0];

            // strings is an array of {command, parameters}, unless there are no parameters to the command
            if (strings.Length == 2)
            {
                // This contains the entire string of parameters.
                var parameters = strings[1];

                string lastParam = null;
                
                if (parameters.StartsWith(":"))
                {
                    // The entire parameter string is a single parameter.
                    lastParam = parameters.Substring(1);
                    parameters = string.Empty;
                }
                
                if (parameters.Contains(" :"))
                {
                    // everything after this is a parameter. The +2 magic value == length of separator to exclude it
                    lastParam = parameters.Substring(parameters.IndexOf(" :", StringComparison.InvariantCulture) + 2);
                    parameters = parameters.Substring(0, parameters.IndexOf(" :", StringComparison.InvariantCulture));
                }
                
                messageParameters = parameters.Split(separator, StringSplitOptions.RemoveEmptyEntries).ToList();

                if (lastParam != null)
                {
                    messageParameters.Add(lastParam);
                }
            }

            return new Message(prefix, command, messageParameters, tags);
        }

        public override string ToString()
        {
            var result = string.Empty;
            if (!string.IsNullOrEmpty(this.Prefix))
            {
                result += ":" + this.Prefix + " ";
            }

            result += this.Command;

            foreach (var p in this.Parameters)
            {
                if (p.Contains(" "))
                {
                    result += " :" + p;
                }
                else
                {
                    result += " " + p;
                }
            }

            return result;
        }
    }
}
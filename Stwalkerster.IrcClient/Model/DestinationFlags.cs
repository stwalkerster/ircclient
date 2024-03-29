﻿namespace Stwalkerster.IrcClient.Model
{
    public class DestinationFlags
    {
        /// <summary>
        /// The channel operators.
        /// </summary>
        public static DestinationFlags ChannelOperators = new DestinationFlags("@");

        /// <summary>
        /// The voiced users.
        /// </summary>
        public static DestinationFlags VoicedUsers = new DestinationFlags("+");

        /// <summary>
        /// Gets the flag.
        /// </summary>
        public string Flag { get; private set; }

        public static DestinationFlags FromChar(string flag)
        {
            switch (flag)
            {
                case "@":
                    return ChannelOperators;
                case "+":
                    return VoicedUsers;
                case "":
                    return null;
                default:
                    return new DestinationFlags(flag);
            }
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="DestinationFlags"/> class.
        /// </summary>
        /// <param name="flag">
        /// The flag.
        /// </param>
        private DestinationFlags(string flag)
        {
            this.Flag = flag;
        }
    }
}

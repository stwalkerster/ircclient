An IRC Client library

![TeamCity build status](https://teamcity.stwalkerster.co.uk/app/rest/builds/buildType:id:Irc_StwalkersterIrcClient_StwalkersterIrcClient/statusIcon.svg)

## Usage

Configure Microsoft.Extensions.Logging appropriately. For example, using log4net:

```
<PackageReference Include="Microsoft.Extensions.Logging.Log4Net.AspNetCore" Version="5.0.4" />
```
```c#
using Microsoft.Extensions.Logging;
// ...
var loggerFactory = new LoggerFactory().AddLog4Net("log4net.xml");
```

Create a configuration object:

```c#
var configuration = new IrcConfiguration(
                hostname: "irc.libera.chat",
                port: 6697,
                authToServices: true,
                nickname: "stwtestbot",
                username: "stwtestbot",
                realName: "stwtestbot",
                servicesUsername: "testbot",
                servicesPassword: "sup3rs3cr3t",
                // OR USE
                servicesCertificate: "/path/to/passwordless/store.pfx",
                ssl: true,
                clientName: "TestClient",
                restartOnHeavyLag: false
            );
```
Create yourself an IrcClient object, and join a channel:
```c#
var client = new IrcClient(
    loggerFactory, 
    configuration, 
    new SupportHelper(loggerFactory.CreateLogger<SupportHelper>)
);

client.JoinChannel("##stwalkerster-development");
```

Finally, make your new bot do something:
```c#
client.ReceivedMessage += (sender, args) =>
{
    if (!args.IsNotice)
    {
        args.Client.SendMessage("##stwalkerster-development", args.User.ToString() + " -> " + args.Client.Nickname);
    }
};

client.Mode("##stwalkerster-development", "+t");
```

### Various notes:
* If you use servicesCertificate, the client will attempt to do a SASL EXTERNAL authentication. You must enable SSL if you want to use this, as it does mTLS negotiation.
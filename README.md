An IRC Client library

## Usage

In-depth knowledge of [Castle Windsor](https://github.com/castleproject/Windsor/blob/master/docs/README.md) is assumed here.

Grab yourself the following [NuGet](https://docs.microsoft.com/en-us/nuget/what-is-nuget) packages:
```xml
<package id="Castle.Core-log4net" version="4.2.1" targetFramework="net45" />
<package id="Castle.LoggingFacility" version="4.1.0" targetFramework="net45" />
<package id="Stwalkerster.IrcClient" version="7.0.11" targetFramework="net45" />

<!-- these should be installed as dependencies of the above -->
<package id="Castle.Core" version="4.2.1" targetFramework="net45" />
<package id="Castle.Windsor" version="4.1.0" targetFramework="net45" />
<package id="log4net" version="2.0.8" targetFramework="net45" />
```

Create yourself a basic [installer](https://github.com/castleproject/Windsor/blob/master/docs/installers.md):
```csharp
public class Installer : IWindsorInstaller
{
    public void Install(IWindsorContainer container, IConfigurationStore store)
    {
        container.AddFacility<LoggingFacility>(f => f.LogUsing<Log4netFactory>().WithConfig("log4net.xml"));
        container.AddFacility<StartableFacility>(x => x.DeferredStart());

        container.Register(
            Component.For<ISupportHelper>().ImplementedBy<SupportHelper>(),
            Component.For<IIrcConfiguration>().Instance(new IrcConfiguration(/* ... */)),
            Component.For<IIrcClient>().ImplementedBy<IrcClient>().Start()
        );
    }
}
```

Configure log4net.

Finally, grab your IRC client from the container:
```csharp
var windsorContainer = new WindsorContainer();
windsorContainer.Install(FromAssembly.This());
var ircClient = windsorContainer.Resolve<IIrcClient>();
```

Remember that the IRC client implements [IStartable](https://github.com/castleproject/Windsor/blob/master/docs/startable-facility.md), so it's probably better to register a managing class, and feed the client in as a dependency:
```csharp
public class Program
{
    public Program(IIrcClient client)
    {
        client.JoinChannel("#en.wikipedia");
    }
}
```

If you do this, remember to register the `Program` component in your installer:
```csharp
Component.For<Program>(),
```

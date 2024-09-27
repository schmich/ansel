using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;

class ProxyHandler(ICommandHandler handler, Func<InvocationContext, ICommandHandler, Task<int>> proxy) : ICommandHandler
{
    public int Invoke(InvocationContext context) => throw new NotImplementedException();
    public Task<int> InvokeAsync(InvocationContext ctx) => proxy(ctx, handler);
}

class AppBuilder
{
    Func<ICommandHandler, ICommandHandler> _wrapHandler = h => h;
    Func<CancellationToken> _createCancellation = () => CancellationToken.None;
    RootCommand _root;

    public AppBuilder()
    {
        _root = new RootCommand();
    }

    public AppBuilder Name(string name, string description)
    {
        _root.Name = name;
        _root.Description = description;
        return this;
    }

    public AppBuilder GlobalHandler<T1>(Func<InvocationContext, ICommandHandler, T1, Task<int>> proxy, Option<T1> d1)
    {
        _root.AddGlobalOption(d1);

        _wrapHandler = handler => new ProxyHandler(handler, (ctx, handler) =>
        {
            var v1 = ctx.ParseResult.GetValueForOption(d1)!;
            return proxy(ctx, handler, v1);
        });

        return this;
    }

    public AppBuilder GlobalHandler<T1, T2>(Func<InvocationContext, ICommandHandler, T1, T2, Task<int>> proxy, Option<T1> d1, Option<T2> d2)
    {
        _root.AddGlobalOption(d1);
        _root.AddGlobalOption(d2);

        _wrapHandler = handler => new ProxyHandler(handler, (ctx, handler) =>
        {
            var v1 = ctx.ParseResult.GetValueForOption(d1)!;
            var v2 = ctx.ParseResult.GetValueForOption(d2)!;
            return proxy(ctx, handler, v1, v2);
        });

        return this;
    }

    public AppBuilder GlobalHandler<T1, T2, T3>(Func<InvocationContext, ICommandHandler, T1, T2, T3, Task<int>> proxy, Option<T1> d1, Option<T2> d2, Option<T3> d3)
    {
        _root.AddGlobalOption(d1);
        _root.AddGlobalOption(d2);
        _root.AddGlobalOption(d3);

        _wrapHandler = handler => new ProxyHandler(handler, (ctx, handler) =>
        {
            var v1 = ctx.ParseResult.GetValueForOption(d1)!;
            var v2 = ctx.ParseResult.GetValueForOption(d2)!;
            var v3 = ctx.ParseResult.GetValueForOption(d3)!;
            return proxy(ctx, handler, v1, v2, v3);
        });

        return this;
    }

    public AppBuilder GlobalHandler<T1, T2, T3, T4>(Func<InvocationContext, ICommandHandler, T1, T2, T3, T4, Task<int>> proxy, Option<T1> d1, Option<T2> d2, Option<T3> d3, Option<T4> d4)
    {
        _root.AddGlobalOption(d1);
        _root.AddGlobalOption(d2);
        _root.AddGlobalOption(d3);
        _root.AddGlobalOption(d4);

        _wrapHandler = handler => new ProxyHandler(handler, (ctx, handler) =>
        {
            var v1 = ctx.ParseResult.GetValueForOption(d1)!;
            var v2 = ctx.ParseResult.GetValueForOption(d2)!;
            var v3 = ctx.ParseResult.GetValueForOption(d3)!;
            var v4 = ctx.ParseResult.GetValueForOption(d4)!;
            return proxy(ctx, handler, v1, v2, v3, v4);
        });

        return this;
    }

    public AppBuilder GlobalHandler<T1, T2, T3, T4, T5>(Func<InvocationContext, ICommandHandler, T1, T2, T3, T4, T5, Task<int>> proxy, Option<T1> d1, Option<T2> d2, Option<T3> d3, Option<T4> d4, Option<T5> d5)
    {
        _root.AddGlobalOption(d1);
        _root.AddGlobalOption(d2);
        _root.AddGlobalOption(d3);
        _root.AddGlobalOption(d4);
        _root.AddGlobalOption(d5);

        _wrapHandler = handler => new ProxyHandler(handler, (ctx, handler) =>
        {
            var v1 = ctx.ParseResult.GetValueForOption(d1)!;
            var v2 = ctx.ParseResult.GetValueForOption(d2)!;
            var v3 = ctx.ParseResult.GetValueForOption(d3)!;
            var v4 = ctx.ParseResult.GetValueForOption(d4)!;
            var v5 = ctx.ParseResult.GetValueForOption(d5)!;
            return proxy(ctx, handler, v1, v2, v3, v4, v5);
        });

        return this;
    }

    public AppBuilder Cancellation(Func<CancellationToken> createCancellation)
    {
        _createCancellation = createCancellation;
        return this;
    }

    public AppBuilder Command(Action<CommandBuilder> action)
    {
        var cmd = new Command(".");
        _root.AddCommand(cmd);

        var builder = new CommandBuilder(cmd, _wrapHandler, _createCancellation);
        action(builder);

        return this;
    }

    public Parser Build()
    {
        return new CommandLineBuilder(_root)
            .UseVersionOption()
            .UseHelp()
            .UseEnvironmentVariableDirective()
            .UseParseDirective()
            .UseSuggestDirective()
            .RegisterWithDotnetSuggest()
            .UseTypoCorrections()
            .UseParseErrorReporting()
            .Build();
    }
}

class CommandBuilder
{
    Command _cmd;
    Func<ICommandHandler, ICommandHandler> _wrapHandler;
    Func<CancellationToken> _createCancellation;

    public CommandBuilder(Command cmd, Func<ICommandHandler, ICommandHandler> wrapHandler, Func<CancellationToken> createCancellation)
    {
        _cmd = cmd;
        _wrapHandler = wrapHandler;
        _createCancellation = createCancellation;
    }

    public CommandBuilder Name(string name, string description)
    {
        _cmd.Name = name;
        _cmd.Description = description;
        return this;
    }

    public CommandBuilder Alias(string alias)
    {
        _cmd.AddAlias(alias);
        return this;
    }

    public CommandBuilder Around(Func<InvocationContext, ICommandHandler, Task<int>> proxy)
    {
        var prevWrap = _wrapHandler;
        var newWrap = (ICommandHandler handler) => new ProxyHandler(handler, proxy);
        _wrapHandler = handler => prevWrap(newWrap(handler));
        return this;
    }

    public CommandBuilder Handler(Func<CancellationToken, Task> handler)
    {
        _cmd.SetHandler(() => handler(_createCancellation()));
        _cmd.Handler = _wrapHandler(_cmd.Handler!);
        return this;
    }

    public CommandBuilder Handler<T1>(Func<T1, CancellationToken, Task> handler, IValueDescriptor<T1> d1)
    {
        var ds = new IValueDescriptor[] { d1 };
        AddDescriptors(ds);

        _cmd.SetHandler((T1 v1) => handler(v1, _createCancellation()), d1);
        _cmd.Handler = _wrapHandler(_cmd.Handler!);

        return this;
    }

    public CommandBuilder Handler<T1, T2>(Func<T1, T2, CancellationToken, Task> handler, IValueDescriptor<T1> d1, IValueDescriptor<T2> d2)
    {
        var ds = new IValueDescriptor[] { d1, d2 };
        AddDescriptors(ds);

        _cmd.SetHandler((T1 v1, T2 v2) => handler(v1, v2, _createCancellation()), d1, d2);
        _cmd.Handler = _wrapHandler(_cmd.Handler!);

        return this;
    }

    public CommandBuilder Handler<T1, T2, T3>(Func<T1, T2, T3, CancellationToken, Task> handler, IValueDescriptor<T1> d1, IValueDescriptor<T2> d2, IValueDescriptor<T3> d3)
    {
        var ds = new IValueDescriptor[] { d1, d2, d3 };
        AddDescriptors(ds);

        _cmd.SetHandler((T1 v1, T2 v2, T3 v3) => handler(v1, v2, v3, _createCancellation()), d1, d2, d3);
        _cmd.Handler = _wrapHandler(_cmd.Handler!);

        return this;
    }

    public CommandBuilder Handler<T1, T2, T3, T4>(Func<T1, T2, T3, T4, CancellationToken, Task> handler, IValueDescriptor<T1> d1, IValueDescriptor<T2> d2, IValueDescriptor<T3> d3, IValueDescriptor<T4> d4)
    {
        var ds = new IValueDescriptor[] { d1, d2, d3, d4 };
        AddDescriptors(ds);

        _cmd.SetHandler((T1 v1, T2 v2, T3 v3, T4 v4) => handler(v1, v2, v3, v4, _createCancellation()), d1, d2, d3, d4);
        _cmd.Handler = _wrapHandler(_cmd.Handler!);

        return this;
    }

    public CommandBuilder Handler<T1, T2, T3, T4, T5>(Func<T1, T2, T3, T4, T5, CancellationToken, Task> handler, IValueDescriptor<T1> d1, IValueDescriptor<T2> d2, IValueDescriptor<T3> d3, IValueDescriptor<T4> d4, IValueDescriptor<T5> d5)
    {
        var ds = new IValueDescriptor[] { d1, d2, d3, d4, d5 };
        AddDescriptors(ds);

        _cmd.SetHandler((T1 v1, T2 v2, T3 v3, T4 v4, T5 v5) => handler(v1, v2, v3, v4, v5, _createCancellation()), d1, d2, d3, d4, d5);
        _cmd.Handler = _wrapHandler(_cmd.Handler!);

        return this;
    }

    public CommandBuilder Handler<T1, T2, T3, T4, T5, T6>(Func<T1, T2, T3, T4, T5, T6, CancellationToken, Task> handler, IValueDescriptor<T1> d1, IValueDescriptor<T2> d2, IValueDescriptor<T3> d3, IValueDescriptor<T4> d4, IValueDescriptor<T5> d5, IValueDescriptor<T6> d6)
    {
        var ds = new IValueDescriptor[] { d1, d2, d3, d4, d5, d6 };
        AddDescriptors(ds);

        _cmd.SetHandler((T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6) => handler(v1, v2, v3, v4, v5, v6, _createCancellation()), d1, d2, d3, d4, d5, d6);
        _cmd.Handler = _wrapHandler(_cmd.Handler!);

        return this;
    }

    public CommandBuilder Handler<T1, T2, T3, T4, T5, T6, T7>(Func<T1, T2, T3, T4, T5, T6, T7, CancellationToken, Task> handler, IValueDescriptor<T1> d1, IValueDescriptor<T2> d2, IValueDescriptor<T3> d3, IValueDescriptor<T4> d4, IValueDescriptor<T5> d5, IValueDescriptor<T6> d6, IValueDescriptor<T7> d7)
    {
        var ds = new IValueDescriptor[] { d1, d2, d3, d4, d5, d6, d7 };
        AddDescriptors(ds);

        _cmd.SetHandler((T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7) => handler(v1, v2, v3, v4, v5, v6, v7, _createCancellation()), d1, d2, d3, d4, d5, d6, d7);
        _cmd.Handler = _wrapHandler(_cmd.Handler!);

        return this;
    }

    public CommandBuilder Command(Action<CommandBuilder> action)
    {
        var cmd = new Command(".");
        _cmd.AddCommand(cmd);

        var builder = new CommandBuilder(cmd, _wrapHandler, _createCancellation);
        action(builder);

        return this;
    }

    void AddDescriptors(IValueDescriptor[] descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            if (descriptor is Option o)
            {
                _cmd.AddOption(o);
            }
            else if (descriptor is Argument a)
            {
                _cmd.AddArgument(a);
            }
        }
    }
}

static class OptionExtensions
{
    public static Option<T> WithMultipleArguments<T>(this Option<T> option, bool allowMultipleArgumentsPerToken)
    {
        option.AllowMultipleArgumentsPerToken = allowMultipleArgumentsPerToken;
        return option;
    }

    public static Option<T> WithRequired<T>(this Option<T> option, bool isRequired)
    {
        option.IsRequired = isRequired;
        return option;
    }
}
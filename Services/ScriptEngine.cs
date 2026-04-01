using System.Windows.Media;
using Jint;
using NLua;
using PrismPane_Widgets.Models;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace PrismPane_Widgets.Services;

public class ScriptEngine
{
    private readonly Dictionary<string, object> _exposedApi = [];

    public void ExposeApi(string name, object api)
    {
        _exposedApi[name] = api;
    }

    public ScriptResult RunJavaScript(string code)
    {
        var result = new ScriptResult();
        try
        {
            var engine = new Engine(cfg => cfg.LimitRecursion(64).TimeoutInterval(TimeSpan.FromSeconds(5)));
            foreach (var (name, api) in _exposedApi)
                engine.SetValue(name, api);

            engine.Execute(code);
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }
        return result;
    }

    public ScriptResult RunLua(string code)
    {
        var result = new ScriptResult();
        try
        {
            using var lua = new NLua.Lua();
            foreach (var (name, api) in _exposedApi)
                lua[name] = api;

            lua.DoString(code);
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }
        return result;
    }

    public ScriptResult Run(string code, ScriptType type) =>
        type == ScriptType.JavaScript ? RunJavaScript(code) : RunLua(code);
}

public class ScriptResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// API object exposed to scripts so they can interact with the taskbar.
/// </summary>
public class TaskbarScriptApi
{
    private readonly Action<string, string> _notify;
    private readonly Action<string, Color> _setColor;

    public TaskbarScriptApi(Action<string, string> notify, Action<string, Color> setColor)
    {
        _notify = notify;
        _setColor = setColor;
    }

    public void notify(string title, string message) => _notify(title, message);

    public void setForegroundColor(string elementName, string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            Application.Current.Dispatcher.Invoke(() => _setColor(elementName, color));
        }
        catch { }
    }

    public string getTime() => DateTime.Now.ToString("HH:mm:ss");
    public string getDate() => DateTime.Now.ToString("yyyy-MM-dd");
}

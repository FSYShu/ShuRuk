using ShuRuk.Runtime.Abstractions.Interfaces;

namespace ShuRuk.App.Helpers;

// Static accessor for IAppContext / IAppContext的静态访问器
// Decouples UI pages from the concrete App class / 解耦UI页面与具体App类
public static class AppContextAccessor
{
    public static IAppContext Current { get; set; } = null!;
}
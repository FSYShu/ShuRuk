using ShuRuk.Contracts.Interfaces;

namespace ShuRuk.App;

// Static accessor for IAppContext / IAppContext的静态访问器
// Decouples UI pages from the concrete App class / 解耦UI页面与具体App类
public static class AppContextAccessor
{
    public static IAppContext Current { get; set; } = null!;
}
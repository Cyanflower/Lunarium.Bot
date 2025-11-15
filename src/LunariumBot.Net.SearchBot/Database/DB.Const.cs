
using System.Net.Sockets;

// NuGet 包依赖
using Npgsql;
using Dapper;
using Polly;
using Polly.Retry;

// 解决方案内引用项目
using LunariumBot.Net.Logger;

// 项目内命名空间
using LunariumBot.Net.SearchBot.Utils;

namespace LunariumBot.Net.SearchBot.Database;

/// <summary>
/// 负责与 PostgreSQL 数据库进行交互的服务。
/// 管理连接、按需执行数据库结构迁移。
/// </summary>
public partial class DatabaseService : IDisposable
{
    // 成功 INSERT 标识符
    public const string InsertedStatus = "INSERTED";
}
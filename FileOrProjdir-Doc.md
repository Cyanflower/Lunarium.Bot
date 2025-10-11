> tree -I 'dist|bin|obj|.vs|.vscode|packages|node_modules|Debug|Release|*.suo|*.user|*.cache|TestResults|.git|.idea|Backup*|UpgradeLog*.htm|_UpgradeReport_Files'
.
├── Database
│   └── docker-compose.yml
├── Discord.Net
├── FileOrProjdir-Doc.md
├── LunariumBot.Net.sln
├── my-postgres.conf
├── README.md
├── src
│   ├── LunariumBot.Net.Archive
│   │   ├── AdminCommand.README.md
│   │   ├── AppInteractionService
│   │   │   ├── AdminCommands.cs
│   │   │   └── PriorityArchiveCommands.cs
│   │   ├── appsettings.jsonc
│   │   ├── appsettings.template.jsonc
│   │   ├── ArchiveBot.cs
│   │   ├── Archiver
│   │   │   ├── Archiver.cs
│   │   │   └── PriorityArchiveDataLoader.cs
│   │   ├── AuthUtils.cs
│   │   ├── ConfigLoader.cs
│   │   ├── GeneralEmbedBuilder.cs
│   │   ├── LunariumBot.Net.Archive.csproj
│   │   ├── Model
│   │   │   ├── ConfigModel.cs
│   │   │   └── PriorityArchiveDataModel.cs
│   │   ├── PriorityArchiveCommand.README.md
│   │   ├── Program.cs
│   │   ├── start.sh
│   │   └── update.sh
│   ├── LunariumBot.Net.Examples
│   │   ├── AppInteractionService
│   │   │   └── AuthUtils.cs
│   │   ├── appsettings.template.jsonc
│   │   ├── ConfigLoader.cs
│   │   ├── ExamplesBot.cs
│   │   ├── LunariumBot.Net.ExampleBot.csproj
│   │   ├── LunariumLogLib_README.md
│   │   ├── Model
│   │   │   ├── ConfigModel.cs
│   │   │   └── ProxyConfig.cs
│   │   ├── Program.cs
│   │   ├── README.md
│   │   ├── start.sh
│   │   └── Utils
│   │       └── GeneralEmbedBuilder.cs
│   ├── LunariumBot.Net.Extensions
│   │   ├── ExtensionMethod.cs
│   │   └── LunariumBot.Net.Extensions.csproj
│   ├── LunariumBot.Net.Logger
│   │   ├── Extensions
│   │   │   ├── LogEntryExtensions.cs
│   │   │   └── MicrosoftLoggingBridge.cs
│   │   ├── Formatting
│   │   │   ├── AnsiColorExtensions.cs
│   │   │   ├── ConsoleFormatter.cs
│   │   │   └── FileFormatter.cs
│   │   ├── ILogger.cs
│   │   ├── Interfaces
│   │   │   └── ILogSink.cs
│   │   ├── LogChannelBridge.cs
│   │   ├── LoggerBuilder.cs
│   │   ├── LoggerBuilderExtensions.cs
│   │   ├── LoggerExtensions.cs
│   │   ├── LoggerWrapper.cs
│   │   ├── LunariumBot.Net.Logger.csproj
│   │   ├── LunariumLogger.cs
│   │   ├── Model.cs
│   │   └── Sink
│   │       ├── ChannelSink.cs
│   │       ├── ConsoleSink.cs
│   │       └── FileSink.cs
│   ├── LunariumBot.Net.SearchBot
│   │   ├── 1.md
│   │   ├── 2.md
│   │   ├── AppInteractionService
│   │   │   ├── AppsMenu.cs
│   │   │   ├── InteractionsCallback
│   │   │   │   ├── FavoriteInteraction.cs
│   │   │   │   ├── GeneralInteraction.cs
│   │   │   │   ├── InteractivePaginatorInteraction.cs
│   │   │   │   ├── PaginatorInteraction.cs
│   │   │   │   ├── QuickSearchInteraction.cs
│   │   │   │   └── SubscriptionsInteraction.cs
│   │   │   ├── InteractionUtils
│   │   │   │   ├── AuthUtils.cs
│   │   │   │   ├── AutoCompletion.cs
│   │   │   │   ├── InteractionUtils.cs
│   │   │   │   ├── SearchUtils.cs
│   │   │   │   └── Utils.cs
│   │   │   └── SlashCommands
│   │   │       ├── AdminCommands.cs
│   │   │       ├── BlockCommands.cs
│   │   │       ├── FavoriteCommands.cs
│   │   │       ├── SearchCommands.cs
│   │   │       ├── SubscriptionCommands.cs
│   │   │       └── UserCommands.cs
│   │   ├── appsettings.jsonc
│   │   ├── appsettings.template.jsonc
│   │   ├── ConfigLoader.cs
│   │   ├── cs_analyzer.py
│   │   ├── Data
│   │   │   └── SearchBot.db
│   │   ├── Database
│   │   │   ├── CRUD
│   │   │   │   ├── CU.Base.cs
│   │   │   │   ├── CU.D.BlockedAuthors.cs
│   │   │   │   ├── CU.Favorite.cs
│   │   │   │   ├── CU.QuickSearch.cs
│   │   │   │   ├── CU.ServerData.cs
│   │   │   │   ├── CU.Subscriptions.cs
│   │   │   │   ├── D.Base.cs
│   │   │   │   ├── D.Favorite.cs
│   │   │   │   ├── D.QuickSearch.cs
│   │   │   │   ├── D.ServerData.cs
│   │   │   │   ├── D.Subscriptions.cs
│   │   │   │   ├── R.Analysis.cs
│   │   │   │   ├── R.Base.cs
│   │   │   │   ├── R.Favorite.cs
│   │   │   │   ├── R.Search.cs
│   │   │   │   └── R.Subscriptions.cs
│   │   │   ├── DatabaseService.cs
│   │   │   └── Table.cs
│   │   ├── log_refactor.py
│   │   ├── LunariumBot.Net.SearchBot.csproj
│   │   ├── Model
│   │   │   ├── ConfigModel.cs
│   │   │   ├── DataModel.cs
│   │   │   └── ProtoModel.cs
│   │   ├── PerformanceAnalysis.html
│   │   ├── PerformanceMonitoringService.cs
│   │   ├── Program.cs
│   │   ├── Protos
│   │   │   └── monitoring.proto
│   │   ├── Resources
│   │   │   ├── char_state_tab.json
│   │   │   ├── dict.txt
│   │   │   ├── idf.txt
│   │   │   ├── pos_prob_emit.json
│   │   │   ├── pos_prob_start.json
│   │   │   ├── pos_prob_trans.json
│   │   │   ├── prob_emit.json
│   │   │   ├── prob_trans.json
│   │   │   └── stopwords.txt
│   │   ├── Scheduler
│   │   │   ├── Job
│   │   │   │   ├── AnalysisJob.cs
│   │   │   │   └── FullSyncJob.cs
│   │   │   ├── PaginatorManager.cs
│   │   │   └── UserCooldown.cs
│   │   ├── SearchBot.cs
│   │   ├── ThreadFetcher
│   │   │   ├── CreatorAutomatic.cs
│   │   │   ├── Debouncer.cs
│   │   │   ├── Listener.cs
│   │   │   ├── Scanner.cs
│   │   │   └── ThreadProcessor.cs
│   │   ├── token.txt
│   │   └── Utils
│   │       ├── DataAnalysis.cs
│   │       ├── DateParser.cs
│   │       ├── DisplayEmbedBuilder.cs
│   │       ├── GeneralEmbedBuilder.cs
│   │       ├── SearchCriteriaBuilder.cs
│   │       └── Tokenizer.cs
│   └── Lunarium.NET.DataCenterBot
│       ├── AppInteractionService
│       │   ├── InteractionUtils
│       │   │   └── AuthUtils.cs
│       │   └── SlashCommands
│       │       └── AdminCommands.cs
│       ├── appsettings.jsonc
│       ├── ConfigLoader.cs
│       ├── Database
│       │   ├── CRUD
│       │   │   ├── DB.ChannelProcess.cs
│       │   │   ├── DB.MessageProcess.cs
│       │   │   └── DB.ThreadProcess.cs
│       │   └── DatabaseService.cs
│       ├── DataCenterAssistBot.cs
│       ├── DataCenterMainBot.cs
│       ├── DataFetcher
│       │   ├── Listener.cs
│       │   ├── Scanner1.cs
│       │   ├── Scanner.cs
│       │   └── ThreadProcessor.cs
│       ├── DataProcess
│       │   ├── ChannelProcessor.cs
│       │   ├── DataProcess.cs
│       │   ├── MessageProcess.cs
│       │   └── ThreadProcessor.cs
│       ├── DBMigrations
│       │   └── v1.initial_schema.pgsql
│       ├── InitializationService.cs
│       ├── LunariumBot.NET.DataCenter.csproj
│       ├── Model
│       │   ├── ConfigModel.cs
│       │   ├── DataModel.cs
│       │   └── ProtoModel.cs
│       ├── Program.cs
│       ├── Protos
│       │   └── monitoring.proto
│       ├── Resources
│       │   ├── char_state_tab.json
│       │   ├── dict.txt
│       │   ├── idf.txt
│       │   ├── pos_prob_emit.json
│       │   ├── pos_prob_start.json
│       │   ├── pos_prob_trans.json
│       │   ├── prob_emit.json
│       │   ├── prob_trans.json
│       │   └── stopwords.txt
│       ├── Scheduler
│       │   └── Job
│       │       ├── AnalysisJob.cs
│       │       └── FullSyncJob.cs
│       └── Utils
│           ├── DataAnalysis.cs
│           ├── DisplayEmbedBuilder.cs
│           ├── GeneralEmbedBuilder.cs
│           └── Tokenizer.cs
└── tests
    └── LunariumBot.Net.Logger.Tests
        ├── Extensions
        │   └── LogEntryExtensionsTests.cs
        ├── Formatting
        │   ├── AnsiColorExtensionsTests.cs
        │   ├── ConsoleFormatterTests.cs
        │   └── FileFormatterTests.cs
        ├── LoggerWrapperTests.cs
        ├── LunariumBot.Net.Logger.Tests.csproj
        ├── LunariumLoggerTests.cs
        └── Sink
            └── FileSinkTests.cs

解决方案内目录结构讲解:
任何 bin 和 obj 或 dist 都是需要忽略的 SDK 自动生成文件/文件夹
- 在根目录下有一个 Discord.Net 文件夹, 为本解决方案自行维护的修改部分源码的 Discord.Net 库源码, 作为解决方案内项目供其他部分项目引用, 该项目无需处理, 知道存在即可
- Database: postgreSQL 的 docker 配置文件 docker-compose.yml
- src: 本解决方案所有项目所在的目录

src内项目:
- Lunarium.NET.DataCenterBot
- LunariumBot.Net.Archive
- LunariumBot.Net.Examples
- LunariumBot.Net.Extensions
- LunariumBot.Net.Logger
- LunariumBot.Net.SearchBot
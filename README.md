# DotHide (ドットファイル自動隠し常駐ツール)

Windows環境において、名前がドット（`.`）で始まるファイルおよびディレクトリ（`.env`, `.gitignore`, `.config`, `.git` 等）を自動的に検知し、Windowsの隠し属性（`FILE_ATTRIBUTE_HIDDEN`）を付与するバックグラウンド常駐ツールです。

---

## 🌟 主な特徴

1. **高速な直接 Win32 API 呼び出し:**
   - `cmd.exe` や `attrib.exe` などの外部プロセスの起動オーバーヘッドを一切排除。
   - `GetFileAttributesW` および `SetFileAttributesW` を P/Invoke 経由で直接呼び出します。

2. **初回同期（フルスキャン）と再帰除外の最適化:**
   - 起動時に監視対象パス配下を再帰スキャンし、未設定のドットファイル/フォルダに隠し属性を付与します。
   - **パフォーマンス対策:** `.git` や `node_modules` など、指定された特定ディレクトリ**内部**への再帰走査はスキップします（※ディレクトリ自体の名前が `.` で始まる場合、フォルダ自体の属性は隠し属性にします）。

3. **リアルタイム監視機構 (`FileSystemWatcher` + 非同期パイプライン):**
   - ファイル/ディレクトリの `Created`（作成）および `Renamed`（リネーム）イベントを常時監視。
   - 例: 通常のファイル `sample.env` が `.env` にリネームされた場合も即座に検知して隠し属性を付与。
   - イベントをキューイングする非同期 `Channel` により、急激なファイル増殖時もイベントロストやバッファオーバーフローを防止。

4. **堅牢な例外・競合ハンドリング:**
   - ファイル作成直後にエディタや Git、ビルドツールがファイルをロックしているケースに対応し、指数バックオフ（リトライ間隔の徐増）による自動再試行を実装。
   - 一時ファイルの即時削除（`ERROR_FILE_NOT_FOUND`）や権限不足（`ERROR_ACCESS_DENIED`）でもクラッシュせず、ログに記録して安全に継続動作。

5. **コンソールウィンドウなしの常駐 & タスクトレイ管理:**
   - Windows GUIサブシステム（`WinExe`）としてビルドされているため、起動時に黒いコンソールウィンドウが表示されません。
   - タスクトレイ（通知領域）にアイコンが常駐し、右クリックメニューから「フルスキャン実行」「一時停止/再開」「設定を開く」「ログを開く」「スタートアップ有効化」「終了」を簡単に行えます。

---

## 📁 ディレクトリ構成

```text
DotHide/
├── DotHide.sln                    # ソリューションファイル
├── config.sample.json             # 設定ファイルのサンプル
├── README.md                      # 本ドキュメント
├── src/
│   └── DotHide/
│       ├── DotHide.csproj         # プロジェクトファイル (.NET 8 Windows GUI)
│       ├── Program.cs             # エントリーポイント & CLI オプション処理
│       ├── config.json            # ビルド時に出力されるデフォルト設定
│       ├── Config/
│       │   ├── AppConfig.cs       # 設定データモデル（環境変数展開対応）
│       │   └── ConfigManager.cs   # 設定の読込・保存・パス解決
│       ├── Core/
│       │   ├── FileHider.cs       # 属性判定 & 指数バックオフロジック
│       │   ├── FullScanner.cs     # 初回高速再帰スキャン
│       │   └── DirectoryWatcher.cs# リアルタイム監視 & イベントキュー
│       ├── Native/
│       │   └── Win32Methods.cs    # Win32 API (kernel32.dll) P/Invoke
│       ├── Logging/
│       │   └── Logger.cs          # スレッドセーフ・ローテーション対応ロガー
│       ├── Startup/
│       │   └── StartupManager.cs  # レジストリ（HKCU Run）スタートアップ管理
│       └── Tray/
│           └── TrayApplicationContext.cs # タスクトレイUIとバックグラウンド統括
└── tests/
    └── DotHide.Tests/             # xUnit 単体テスト・統合テスト（全22件）
```

---

## 🛠️ ビルド手順

### 必要環境
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 以降
- Windows 10 / Windows 11 / Windows Server (x64 / ARM64)

### 1. リポジトリのクローン
```powershell
git clone <repository-url>
cd DotHide
```

### 2. テストの実行
```powershell
dotnet test
```
*(全22項目のテストが実行され、Win32属性付与、再帰走査、除外パス判定、リアルタイム監視が検証されます)*

### 3. 通常ビルド (Framework-dependent)
```powershell
dotnet publish src/DotHide/DotHide.csproj -c Release -o publish
```

### 4. スタンドアロン実行ファイルの作成 (Self-contained)
実行環境に .NET ランタイムがインストールされていないPCでも単体で動作する単一実行ファイルを生成する場合:
```powershell
dotnet publish src/DotHide/DotHide.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
```
生成物: `publish\DotHide.exe`（これ1つと `config.json` だけで全機能がスタンドアロン動作します）

---

## ⚙️ 設定ファイル (`config.json`)

DotHide は、実行ファイルと同じフォルダの `config.json` または `%APPDATA%\DotHide\config.json` を参照します。ファイルが存在しない場合は初回起動時に自動生成されます。

### サンプル設定 (`config.sample.json`)
```json
{
  "watch_paths": [
    "%USERPROFILE%"
  ],
  "exclude_directories": [
    ".git",
    "node_modules",
    ".vs",
    ".idea",
    "target",
    "bin",
    "obj",
    "$RECYCLE.BIN",
    "AppData"
  ],
  "scan_on_startup": true,
  "retry_count": 5,
  "retry_initial_delay_ms": 50,
  "retry_max_delay_ms": 1000,
  "enable_tray_icon": true,
  "log_level": "Information",
  "log_to_file": true,
  "log_file_path": "%APPDATA%\\DotHide\\dothide.log"
}
```

### 各設定項目の説明

| 設定項目 | 型 | デフォルト値 | 説明 |
| :--- | :--- | :--- | :--- |
| `watch_paths` | 配列 | `["%USERPROFILE%"]` | 監視対象ディレクトリの一覧。`%USERPROFILE%` などの環境変数は自動展開されます。複数パス指定可能。 |
| `exclude_directories` | 配列 | `[".git", "node_modules", ...]` | **内部への再帰走査をスキップするディレクトリ名**。※ディレクトリ自身の先頭が `.` の場合（`.git` 等）、そのディレクトリ自体の隠し属性は付与されます。 |
| `scan_on_startup` | 真偽値 | `true` | アプリ起動時にフルスキャン（初回同期）を実行するかどうか。 |
| `retry_count` | 整数 | `5` | ファイルロック時の最大再試行回数。 |
| `retry_initial_delay_ms` | 整数 | `50` | 再試行時の初期ウェイト時間（ミリ秒）。以降、指数関数的に倍加します。 |
| `retry_max_delay_ms` | 整数 | `1000` | 再試行ウェイトの最大上限値（ミリ秒）。 |
| `enable_tray_icon` | 真偽値 | `true` | タスクトレイ（通知領域）にアイコンを表示するか。`false` にすると完全不可視のデーモンとして常駐します。 |
| `log_level` | 文字列 | `"Information"` | ログ出力レベル (`"Debug"`, `"Information"`, `"Warning"`, `"Error"`)。 |
| `log_to_file` | 真偽値 | `true` | ファイルへのログ記録を行うか。 |
| `log_file_path` | 文字列 | `"%APPDATA%\\DotHide\\dothide.log"` | ログファイルの出力先パス。 |

---

## 🚀 実行およびスタートアップ登録方法

### 1. 常駐バックグラウンド起動
```powershell
.\publish\DotHide.exe
```
コンソール画面は一切開かず、タスクトレイにアイコンが表示されて常駐監視を開始します。

### 2. Windows スタートアップへの登録

#### 方法A: タスクトレイメニューから（GUI）
1. タスクトレイの DotHide アイコンを右クリックします。
2. **「Windows起動時に実行 (&W)」** にチェックを入れます。
3. これだけで次回 Windows ログオン時から自動常駐します。

#### 方法B: コマンドラインから（CLI）
管理者権限不要で、現在のユーザーのログオン時自動起動レジストリ（`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`）に登録されます。
```powershell
# スタートアップに登録
.\publish\DotHide.exe --install-startup

# スタートアップ登録を解除
.\publish\DotHide.exe --uninstall-startup

# 現在の状態を確認
.\publish\DotHide.exe --status
```

### 3. コマンドライン オプション一覧

```text
Usage:
  DotHide.exe [options]

Options:
  --scan-only           監視対象フォルダの初回フルスキャンのみを一度実行して終了します。
  --console             シェルに標準出力をアタッチして実行（動作確認・デバッグ用）。
  --install-startup     Windows起動時（HKCU Run）に自動起動するよう登録します。
  --uninstall-startup   Windows起動時自動起動の登録を解除します。
  --status              現在の設定内容とスタートアップ登録状況を表示します。
  --config <path>       カスタム設定ファイル（config.json）のパスを指定します。
  --help, -h            ヘルプメッセージを表示します。
```


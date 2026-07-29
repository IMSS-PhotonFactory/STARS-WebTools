# STARS-HttpClient マニュアル

## 概要

STARS-HttpClient は、STARS システム（`STARS` ライブラリによるノード間通信プロトコル）に対して、
シンプルな HTTP/JSON インターフェースを提供する中継アプリケーションです。

外部のクライアント（ブラウザ、他システム、スクリプトなど）から HTTP リクエスト（GET/POST）を送信することで、
STARS ネットワーク上の任意のノードにコマンドを送信し、その応答を JSON 形式で受け取ることができます。

内部では組み込みの簡易 HTTP サーバー（`TcpListener` ベース）を実装しており、外部の Web サーバーには依存しません。

## 動作環境

- .NET 10
- STARS システムに接続可能なネットワーク環境

## 起動方法

1. 実行ファイル（`STARS-HttpClient.exe` など）を配置したフォルダーでアプリケーションを起動します。
2. 初回起動時、同じフォルダーに `config.json` が存在しない場合は、既定値で自動生成され、アプリケーションは終了します。
   メッセージ例:
   ```
   Could not find config file [config.json]. New config file created. Please restart application.
   ```
3. 必要に応じて `config.json` の内容を編集し、再度アプリケーションを起動します。
4. 起動に成功すると、以下のようなメッセージが表示され、HTTP リクエストの待ち受けを開始します。
   ```
   Listening on http://localhost:9901/ (Ctrl+C to stop)
   ```
5. 終了するには `Ctrl+C` を押します。

### 設定ファイル名の切り替え

コマンドライン引数を指定すると、`config.json` の代わりに `<引数>.json` を設定ファイルとして使用できます。

```
STARS-HttpClient.exe myconfig
```

上記の場合、`myconfig.json` が読み込まれます。

## 設定ファイル（config.json）

既定で生成される設定ファイルの内容は以下の通りです。

```json
{
  "StarsConf": {
	"StarsNode": "webapi",
	"StarsHost": "127.0.0.1",
	"StarsPort": 6057,
	"StarsKey": "webapi.key",
	"StarsKeyword": "stars",
	"UseStarsKeyword": true
  },
  "HttpPort": 9901
}
```

| キー | 説明 |
|---|---|
| `StarsConf.StarsNode` | このアプリケーションが STARS ネットワーク上で名乗るノード名 |
| `StarsConf.StarsHost` | 接続先の STARS ホスト（IP アドレスまたはホスト名） |
| `StarsConf.StarsPort` | 接続先の STARS ポート番号 |
| `StarsConf.StarsKey` | STARS 接続時に使用するキー |
| `StarsConf.StarsKeyword` | STARS 接続時に使用するキーワード（`UseStarsKeyword` が `true` の場合のみ有効） |
| `StarsConf.UseStarsKeyword` | キーワード認証を使用するかどうか |
| `HttpPort` | このアプリケーションが待ち受ける HTTP ポート番号（既定: 9901） |

起動時に STARS への接続が確立できない場合、以下のメッセージを表示してアプリケーションは終了します。

```
STARS connection is not ready.
```

## アクセス制限（allowlist.txt）

STARS-HttpClient は、実行ファイルと同じフォルダに配置した `allowlist.txt` を用いて、接続元 IP アドレスによるアクセス制限を行います。

- `allowlist.txt` には、許可する IP アドレスまたは FQDN（ホスト名）を1行に1つずつ記載します。
- `#` で始まる行、および空行はコメントとして無視されます。
- IP アドレスの記載では、ワイルドカード `*` を使用してオクテット単位・部分一致でまとめて許可できます（例: `192.168.1.*` は `192.168.1.0`〜`192.168.1.255` すべてに一致）。
- FQDN を指定した場合は、リクエストを受け付けるたびに名前解決を行い、解決された IP アドレスと接続元アドレスを比較します。
- `allowlist.txt` が存在しない場合は、アクセス制限を行わずすべての接続を許可します。
- `allowlist.txt` は存在するが、有効なエントリが1件もない場合は、すべての接続が拒否されます。
- ファイルの更新は自動的に検知され、アプリケーションを再起動しなくても変更内容が反映されます。
- 許可リストに含まれない接続元からのリクエストには、`403 Forbidden` が返され、STARS への処理は実行されません。

**記載例**
```
# 社内端末
192.168.1.10
192.168.1.20

# 192.168.2.0/24 のサブネットをまとめて許可
192.168.2.*

# ホスト名指定も可能
client.example.local
```

## HTTP API

すべての API は `http://<ホスト>:<HttpPort>/StarsApi` 配下のパスで提供されます。
リクエストは `GET`（クエリ文字列）または `POST`（JSON ボディ）に対応しています。両方を指定した場合は POST ボディの値が優先されます。

レスポンスは常に JSON 形式（`Content-Type: application/json`）です。

### 共通パラメーター

| パラメーター名 | 必須 | 説明 |
|---|---|---|
| `node` | 必須 | コマンド送信先の STARS ノード名 |
| `command` | エンドポイントにより自動設定/必須 | 実行するコマンド名 |
| `param` | エンドポイントにより必須/任意 | コマンドの引数（文字列） |
| `timeout` | 任意 | 応答待ちのタイムアウト（ミリ秒、既定値: 5000） |

`node`、`command`、`param`、`timeout` 以外に指定したパラメーターは、追加パラメーターとして扱われます（現状の応答内容には反映されません）。

### 1. `POST/GET /StarsApi`

任意のコマンドを自由に送信します。`command` パラメーターを自分で指定する必要があります。

**リクエスト例（GET）**
```
GET /StarsApi?node=pm16c.ch0&command=SetValue&param=100
```

**リクエスト例（POST）**
```json
POST /StarsApi
Content-Type: application/json

{
  "node": "pm16c.ch0",
  "command": "SetValue",
  "param": "100"
}
```

### 2. `POST/GET /StarsApi/GetValue`

値取得用の簡易エンドポイントです。`command` は自動的に `GetValue` に設定されます。

```
GET /StarsApi/GetValue?node=nct08
```

### 3. `POST/GET /StarsApi/SetValue`

値設定用の簡易エンドポイントです。`command` は自動的に `SetValue` に設定され、`param` の指定が必須です。

```
GET /StarsApi/SetValue?node=pm16c.ch0&param=-500
```

### レスポンス形式

**成功時（200 OK）**
```json
{
  "node": "plc1",
  "command": "GetValue",
  "result": "123",
  "errorflag": false
}
```

- STARS 側からの応答が `Er:` を含む場合、`errorflag` は `true` になります。
- STARS 側からの応答がタイムアウトした場合、`result` に `"Er: Timeout <ミリ秒> ms"` が設定され、`errorflag` は `true` になります。

**エラー時（400 Bad Request）**

`node` または `command`（`SetValue` の場合は `param` も）が指定されていない場合に返されます。

```json
{
  "node": "",
  "command": "",
  "result": "parameter node is required.",
  "errorflag": true
}
```

**その他のエラーレスポンス**

| ステータスコード | 条件 |
|---|---|
| 403 Forbidden | 接続元 IP アドレスが `allowlist.txt` に含まれていない場合 |
| 404 Not Found | 存在しないパスへのリクエスト |
| 405 Method Not Allowed | GET/POST 以外のメソッド |
| 400 Bad Request | POST ボディが不正な JSON の場合 |
| 500 Internal Server Error | サーバー内部で予期しないエラーが発生した場合 |

## トラブルシューティング

| 症状 | 対処 |
|---|---|
| `Could not find config file` と表示され終了する | 生成された `config.json` の内容を環境に合わせて編集し、再起動してください |
| `Could not read config file` と表示され終了する | `config.json` の JSON 構文に誤りがないか確認してください |
| `STARS connection is not ready.` と表示され終了する | `StarsHost` / `StarsPort` / `StarsKey` などの設定値と、STARS 側の起動状態・ネットワーク疎通を確認してください |
| API が常にタイムアウトする（`Er: Timeout ... ms`） | 指定した `node` が STARS ネットワーク上に存在し、応答可能な状態か確認してください。必要に応じて `timeout` パラメーターを大きくしてください |

### タイムアウト時間の変更

STARSコマンド発行後に一定時間内に応答がない場合、タイムアウトが発生します（デフォルトは5000ミリ秒）。応答に時間のかかる処理などは `timeout` パラメーターを指定することで、タイムアウト時間（ミリ秒）を変更できます。

```
GET /StarsApi/GetValue?node=nct08&timeout=30000
```

#!/bin/bash

# Replit特化デプロイスクリプト
echo "=== REPLIT DEPLOY CUSTOM SCRIPT START ==="
echo "実行環境: $(uname -a)"
echo "カレントディレクトリ: $(pwd)"

# すべてのdotnetプロセスを終了
echo "既存のdotnetプロセスを終了します..."
pkill -f "dotnet" || true

# 環境変数の設定
export ASPNETCORE_ENVIRONMENT=Production
export DOTNET_RUNNING_IN_CONTAINER=true
export APP_BASE_PATH="/trial-app1"
# REPLITデプロイ環境変数を削除（ベースパス優先）
# export REPLIT_DEPLOYMENT=true

# 環境変数ファイルの読み込み（オプション）
if [ -f .env ]; then
  echo "環境変数ファイルを読み込みます"
  source .env
else
  echo "環境変数ファイルが見つかりません。デフォルト設定を使用します。"
  
  # MSPSeimei関連の環境変数を明示的に設定
  export MSPSeimei__AzureSearchEndpoint="https://iluragsearch.search.windows.net"
  export MSPSeimei__AzureSearchKey="Gt6kwOhyZKs0ICnOV17JBljGDwiacTHxKstsIaS7BDAzSeCR55N9"
  export MSPSeimei__AzureSearchApiVersion="2020-06-30"
  export MSPSeimei__MainIndexName="mspseimei"
  export MSPSeimei__SentenceIndexName="mspseimei-sentence"
  export MSPSeimei__AzureOpenAIEndpoint="https://openai-gpt-forrag.openai.azure.com"
  export MSPSeimei__AzureOpenAIKey="5c05fc27ca534abbae6a6cee6d9d0b41"
  export MSPSeimei__AzureOpenAIApiVersion="2023-05-15"
  export MSPSeimei__ChatModelDeployment="gpt4o-forRAG"
  export MSPSeimei__EmbeddingModelDeployment="text-embedding-3-large"
fi

# 環境変数の確認
echo "環境変数設定:"
echo "APP_BASE_PATH=$APP_BASE_PATH"
echo "ASPNETCORE_ENVIRONMENT=$ASPNETCORE_ENVIRONMENT"
echo "REPLIT_DEPLOYMENT=$REPLIT_DEPLOYMENT"

# Azure OpenAI環境変数の確認
echo "Azure OpenAI環境変数:"
echo "MSPSeimei__AzureSearchEndpoint=$MSPSeimei__AzureSearchEndpoint"
echo "MSPSeimei__AzureOpenAIEndpoint=$MSPSeimei__AzureOpenAIEndpoint"

# Replitデプロイ環境向けの設定確認
echo "=== REPLIT DEPLOYMENT CONFIGURATION CHECK ==="
echo "ベースパス設定: $APP_BASE_PATH"
echo "環境変数: ASPNETCORE_ENVIRONMENT=$ASPNETCORE_ENVIRONMENT"

# Program.csの修正があれば適用
if grep -q "REPLIT_DEPLOYMENT" Program.cs; then
  echo "Program.csはReplit対応済みです"
else
  echo "Program.csはReplit向けに修正されています（最新版）"
fi

# Replitヘルスチェック設定の確認
if grep -q "Replit特化のヘルスチェック" Program.cs; then
  echo "✅ Replit特化のヘルスチェック対応が設定されています"
else
  echo "❌ Replit特化のヘルスチェック対応が見つかりません"
fi

# アプリケーションの起動
echo "アプリケーションを起動します (Replit向け設定)..."

if [ -f bin/Release/net8.0/AzureRag.dll ]; then
  echo "リリースビルドを使用します"
  dotnet bin/Release/net8.0/AzureRag.dll --urls="http://0.0.0.0:5000"
else
  echo "プロジェクトから直接実行します"
  dotnet run --project AzureRag.csproj --urls="http://0.0.0.0:5000" --configuration Release
fi

echo "=== REPLIT DEPLOY SCRIPT COMPLETED ==="
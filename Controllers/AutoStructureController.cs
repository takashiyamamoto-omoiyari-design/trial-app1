using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using AzureRag.Services;

namespace AzureRag.Controllers
{
    [ApiController]
    [Route("api/AutoStructure")]
    [Authorize]
    public class AutoStructureController : ControllerBase
    {
        private readonly ILogger<AutoStructureController> _logger;
        private readonly IAutoStructureService _autoStructureService;
        private readonly Services.IAuthorizationService _authorizationService;

        public AutoStructureController(
            ILogger<AutoStructureController> logger,
            IAutoStructureService autoStructureService,
            Services.IAuthorizationService authorizationService)
        {
            _logger = logger;
            _autoStructureService = autoStructureService;
            _authorizationService = authorizationService;
        }

        /// <summary>
        /// PDFファイルを受信して処理するエンドポイント
        /// </summary>
        [HttpPost("Analyze")]
        [AllowAnonymous] // 外部API呼び出し用
        [RequestSizeLimit(100_000_000)] // 100MB制限
        public async Task<IActionResult> AnalyzePdf(IFormFile file, [FromForm] string userid, [FromForm] string password, [FromForm] string login_user = null)
        {
            _logger.LogInformation("【AutoStructureController】PDFファイル処理リクエストを受信しました");
            
            try
            {
                // フォームからファイルとパラメータを取得
                var form = await Request.ReadFormAsync();
                var type = form["type"].ToString();
                
                // リクエストパラメータのログ
                _logger.LogInformation($"【AutoStructureController】リクエストパラメータ: type={type}, userid={userid}, password=***");
                _logger.LogInformation($"【AutoStructureController】ファイル情報: {(file != null ? $"ファイル名={file.FileName}, サイズ={file.Length}バイト" : "ファイルなし")}");
                
                // リクエストヘッダー情報のログ
                _logger.LogInformation($"【AutoStructureController】Content-Type: {Request.ContentType}");
                foreach (var header in Request.Headers)
                {
                    if (header.Key != "Authorization" && header.Key != "Cookie")
                    {
                        _logger.LogInformation($"【AutoStructureController】ヘッダー {header.Key}: {string.Join(", ", header.Value)}");
                    }
                }
                
                // 認証チェック
                if (string.IsNullOrEmpty(userid) || string.IsNullOrEmpty(password))
                {
                    _logger.LogWarning("【AutoStructureController】認証情報が不足しています");
                    return BadRequest(new { error = "認証情報が必要です", error_detail = "ユーザーIDとパスワードが必要です" });
                }

                // ファイルチェック
                if (file == null || file.Length == 0)
                {
                    _logger.LogWarning("【AutoStructureController】ファイルが提供されていません");
                    return BadRequest(new { error = "ファイルが提供されていません", error_detail = "PDFファイルを選択してください" });
                }

                // ファイル情報をログに記録
                _logger.LogInformation($"【AutoStructureController】PDFファイル受信: {file.FileName}, サイズ: {file.Length} バイト");
                
                try
                {
                    // 外部APIを呼び出す
                    _logger.LogInformation("【AutoStructureController】外部APIを呼び出します");
                    var result = await _autoStructureService.AnalyzeFileAsync(file, userid, password);
                    
                    _logger.LogInformation($"【AutoStructureController】外部API呼び出し結果: work_id={result.WorkId}, return_code={result.ReturnCode}");
                    
                    // エラーチェック
                    if (result.ReturnCode != 0)
                    {
                        _logger.LogWarning($"【AutoStructureController】外部APIがエラーを返しました: {result.ErrorDetail}");
                        return BadRequest(new { 
                            error = "ファイル処理中にエラーが発生しました", 
                            error_detail = result.ErrorDetail,
                            return_code = result.ReturnCode
                        });
                    }

                    // 🔥 重要: 外部API成功後、ユーザーのworkIdを動的に登録
                    if (!string.IsNullOrEmpty(result.WorkId))
                    {
                        // 実際のログインユーザーを取得（FormDataから優先、フォールバック処理）
                        var actualUser = login_user;
                        
                        // フォールバック処理: login_userが取得できない場合
                        if (string.IsNullOrEmpty(actualUser))
                        {
                            // Cookie認証から取得を試行
                            actualUser = User?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
                            
                            // それでも取得できない場合はuseridを使用（最後の手段）
                            if (string.IsNullOrEmpty(actualUser))
                            {
                                actualUser = userid;
                            }
                        }

                        _logger.LogInformation("【AutoStructureController】ユーザーworkId登録開始: 実際のログインユーザー={ActualUser}, API認証ユーザー={UserId}, workId={WorkId}, ファイル={FileName}", 
                            actualUser, userid, result.WorkId, file.FileName);

                        var workIdRegistered = await _authorizationService.AddWorkIdToUserAsync(
                            actualUser, 
                            result.WorkId, 
                            file.FileName, 
                            $"ユーザー {actualUser} がアップロードしたPDFファイル: {file.FileName}"
                        );

                        if (workIdRegistered)
                        {
                            _logger.LogInformation("【AutoStructureController】ユーザーworkId登録成功: 実際のログインユーザー={ActualUser}, workId={WorkId}", actualUser, result.WorkId);
                        }
                        else
                        {
                            _logger.LogWarning("【AutoStructureController】ユーザーworkId登録失敗: 実際のログインユーザー={ActualUser}, workId={WorkId}", actualUser, result.WorkId);
                            // workId登録失敗してもファイル処理は成功しているので、警告のみでエラーは返さない
                        }
                    }
                    
                    // 成功応答を返す
                    return Ok(new { 
                        work_id = result.WorkId,
                        return_code = 0,
                        error_detail = ""
                    });
                }
                catch (Exception apiEx)
                {
                    _logger.LogError(apiEx, "【AutoStructureController】外部API呼び出し中にエラーが発生しました");
                    return StatusCode(500, new { 
                        error = "外部API呼び出し中にエラーが発生しました", 
                        error_detail = apiEx.Message,
                        return_code = -1
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"【AutoStructureController】処理中にエラーが発生しました: {ex.Message}");
                _logger.LogError($"【AutoStructureController】スタックトレース: {ex.StackTrace}");
                
                return StatusCode(500, new { 
                    error = "内部サーバーエラー", 
                    error_detail = ex.Message,
                    return_code = 1
                });
            }
        }

        /// <summary>
        /// 処理状況を確認するエンドポイント
        /// </summary>
        [HttpPost("Check")]
        [AllowAnonymous] // 外部API呼び出し用
        public async Task<IActionResult> Check(string work_id, string userid, string password)
        {
            _logger.LogInformation($"【AutoStructureController】ステータス確認リクエスト: work_id={work_id}");
            
            try
            {
                // 認証チェック
                if (string.IsNullOrEmpty(userid) || string.IsNullOrEmpty(password))
                {
                    _logger.LogWarning("【AutoStructureController】認証情報が不足しています");
                    return BadRequest(new { 
                        error = "認証情報が必要です", 
                        error_detail = "ユーザーIDとパスワードが必要です",
                        return_code = 1
                    });
                }

                // work_idチェック
                if (string.IsNullOrEmpty(work_id))
                {
                    _logger.LogWarning("【AutoStructureController】work_idが提供されていません");
                    return BadRequest(new { 
                        error = "work_idが必要です", 
                        error_detail = "処理IDを指定してください",
                        return_code = 1
                    });
                }

                // AutoStructureServiceを使って実際のデータを取得
                try
                {
                    // 🔐 重要: workIdアクセス権限チェックを追加
                    var hasAccess = await _authorizationService.CanAccessWorkIdAsync(userid, work_id);
                    if (!hasAccess)
                    {
                        _logger.LogWarning("【AutoStructureController】workIdアクセス拒否: ユーザー={UserId}, workId={WorkId}", userid, work_id);
                        return Forbidden(new { 
                            error = "アクセス権限がありません", 
                            error_detail = $"ユーザー '{userid}' はworkId '{work_id}' にアクセスする権限がありません",
                            return_code = 403
                        });
                    }

                    _logger.LogInformation($"【AutoStructureController】work_id={work_id}のデータを取得します");
                    var structuredData = await _autoStructureService.GetStructuredDataAsync(work_id);
                    
                    if (structuredData == null)
                    {
                        _logger.LogWarning($"【AutoStructureController】work_id={work_id}のデータが見つかりませんでした");
                        return NotFound(new { 
                            error = "指定されたwork_idのデータが見つかりませんでした", 
                            error_detail = $"処理ID: {work_id} のデータは存在しません",
                            return_code = 1
                        });
                    }
                    
                    _logger.LogInformation($"【AutoStructureController】データ取得成功: ChunkList={structuredData.ChunkList?.Count ?? 0}件, TextList={structuredData.TextList?.Count ?? 0}件");
                    
                    // 成功応答を返す - リターンコードを含める
                    var response = structuredData;
                    // JsonElementの追加操作はできないので、return_codeをレスポンスヘッダーに追加
                    Response.Headers.Add("X-Return-Code", "0");
                    return Ok(response);
                }
                catch (Exception dataEx)
                {
                    _logger.LogError($"【AutoStructureController】データ取得中にエラーが発生: {dataEx.Message}");
                    return StatusCode(500, new { 
                        error = "データ取得エラー", 
                        error_detail = dataEx.Message,
                        return_code = 1
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"【AutoStructureController】ステータス確認中にエラーが発生しました: {ex.Message}");
                return StatusCode(500, new { 
                    error = "内部サーバーエラー", 
                    error_detail = ex.Message,
                    return_code = 1
                });
            }
        }

        /// <summary>
        /// サーバー疎通確認用の簡易エンドポイント
        /// </summary>
        [HttpGet("HealthCheck")]
        public IActionResult HealthCheck()
        {
            _logger.LogInformation("【AutoStructureController】疎通確認リクエストを受信しました");
            
            try
            {
                // 成功応答を返す - リターンコードを含める
                return Ok(new { 
                    status = "ok", 
                    message = "サーバー疎通完了", 
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    return_code = 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"【AutoStructureController】疎通確認中にエラーが発生しました: {ex.Message}");
                return StatusCode(500, new { 
                    error = "内部サーバーエラー", 
                    error_detail = ex.Message,
                    return_code = 1
                });
            }
        }

        /// <summary>
        /// Forbiddenレスポンスを返すヘルパーメソッド
        /// </summary>
        private IActionResult Forbidden(object value)
        {
            return StatusCode(403, value);
        }
    }
} 
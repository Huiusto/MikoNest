using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace MiNest.Services;

/// <summary>
/// 视频页（哔哩哔哩客户端「精选」式）：视频全屏占据整个软件页面，
/// 滚轮 / 方向键上下滑动切换视频自动播放；右侧悬浮点赞 / 收藏 / 评论 / 随机，
/// 评论面板可查看与发表评论；支持登录 B 站账号后点赞、关注、评论（C# 侧带 WBI 签名）。
/// </summary>
public static class BiliFeed
{
    public const string Html = """
        <!DOCTYPE html>
        <html lang="zh-CN">
        <head>
        <meta charset="utf-8">
        <title>视频流</title>
        <style>
          * { margin:0; padding:0; box-sizing:border-box; }
          html, body { height:100%; overflow:hidden; background:#000; }
          body { font-family:'Microsoft YaHei UI','PingFang SC',sans-serif; color:#F2F3F5; user-select:none; }
          #v { position:fixed; inset:0; width:100%; height:100%; object-fit:contain; background:#000; }
          .veil { position:fixed; inset:0; pointer-events:none;
                  background:linear-gradient(180deg, rgba(0,0,0,.38) 0%, rgba(0,0,0,0) 28%, rgba(0,0,0,0) 62%, rgba(0,0,0,.42) 100%); }
          .spinner { position:fixed; left:50%; top:46%; transform:translate(-50%,-50%); display:none;
                     width:46px; height:46px; border:4px solid rgba(255,255,255,.25);
                     border-top-color:#00AEEC; border-radius:50%; animation:spin 0.8s linear infinite; }
          @keyframes spin { to { transform:translate(-50%,-50%) rotate(360deg); } }
          .top { position:fixed; top:0; left:0; right:0; display:flex; align-items:center;
                 justify-content:space-between; padding:14px 20px; pointer-events:none; z-index:15; }
          .brand { display:flex; align-items:center; gap:10px; }
          .brand .dot { width:8px; height:8px; border-radius:50%; background:#00AEEC; box-shadow:0 0 12px #00AEEC; }
          .brand b { font-size:17px; letter-spacing:1px; }
          .brand .hint { font-size:11.5px; color:#B4B9C3; margin-left:6px; }
          .btn { pointer-events:auto; cursor:pointer; border:none; border-radius:20px;
                 background:rgba(22,26,34,.78); color:#F2F3F5; font-size:12.5px;
                 padding:8px 16px; backdrop-filter:blur(8px); }
          .btn:hover { background:rgba(40,48,62,.9); }
          .btn .login-ok { color:#7EB3F5; }
          .info { position:fixed; left:24px; bottom:26px; right:120px; z-index:5; }
          .info .title { font-size:18.5px; font-weight:700; line-height:1.4; text-shadow:0 2px 12px rgba(0,0,0,.7);
                         display:-webkit-box; -webkit-line-clamp:2; -webkit-box-orient:vertical; overflow:hidden; }
          .info .author { display:flex; align-items:center; margin-top:10px; gap:10px; }
          .info .author .avatar { width:30px; height:30px; border-radius:50%; background:#2A3140; border:1px solid rgba(255,255,255,.25);
                                  display:flex; align-items:center; justify-content:center; font-size:13px; }
          .info .author .name { font-size:13.5px; color:#D6DAE0; cursor:pointer; }
          .info .author .name:hover { color:#7EB3F5; }
          .info .follow { border:1px solid rgba(251,114,153,.9); background:transparent; color:#FB7299; }
          .info .follow:hover { background:rgba(251,114,153,.16); }
          .info .follow.done { color:#B4B9C3; border-color:rgba(255,255,255,.25); }
          .info .desc { margin-top:10px; font-size:12.5px; color:#B4B9C3; line-height:1.6; max-height:40px;
                        overflow:hidden; display:-webkit-box; -webkit-line-clamp:2; -webkit-box-orient:vertical;
                        cursor:pointer; text-shadow:0 1px 6px rgba(0,0,0,.6); }
          .info .stats { margin-top:8px; font-size:12px; color:#C8CDD6; }
          .rail { position:fixed; right:18px; bottom:120px; display:flex; flex-direction:column; gap:14px; z-index:6; }
          .rail .act { display:flex; flex-direction:column; align-items:center; gap:4px; cursor:pointer; }
          .rail .act .ic { width:50px; height:50px; border-radius:50%; background:rgba(22,26,34,.78);
                           display:flex; align-items:center; justify-content:center; font-size:21px;
                           backdrop-filter:blur(8px); transition:transform .1s; }
          .rail .act:active .ic { transform:scale(.9); }
          .rail .act .cnt { font-size:11.5px; color:#D6DAE0; }
          .rail .act.on .ic { background:rgba(0,174,236,.28); color:#00AEEC; }
          .rail .act.on .cnt { color:#00AEEC; }
          .cmt-panel { position:fixed; top:0; right:-400px; width:380px; height:100%; z-index:20;
                       background:#141823; border-left:1px solid #232936; transition:right .25s ease;
                       display:flex; flex-direction:column; }
          .cmt-panel.open { right:0; }
          .cmt-head { padding:16px 18px 12px; border-bottom:1px solid #1E2530; display:flex; justify-content:space-between; align-items:center; }
          .cmt-head b { font-size:15px; }
          .cmt-close { cursor:pointer; background:none; border:none; color:#8A93A8; font-size:20px; }
          .cmt-list { flex:1; overflow-y:auto; padding:0 16px; }
          .cm { padding:10px 0; border-bottom:1px solid #1E2530; }
          .cm .u { color:#7EB3F5; font-size:12.5px; }
          .cm .m { font-size:13px; margin-top:4px; line-height:1.55; word-break:break-word; }
          .cm .meta { color:#6B7280; font-size:11px; margin-top:4px; }
          .cmt-input { padding:12px 14px; border-top:1px solid #1E2530; display:flex; gap:8px; }
          .cmt-input input { flex:1; background:#0F1115; border:1px solid #232936; border-radius:18px;
                             padding:9px 14px; color:#F2F3F5; font-size:13px; outline:none; }
          .cmt-input button { border:none; border-radius:18px; background:#00AEEC; color:#fff;
                              padding:0 18px; cursor:pointer; font-size:13px; }
          .cmt-input button:disabled { opacity:.5; cursor:default; }
          .toast { position:fixed; left:50%; top:70px; transform:translateX(-50%); z-index:99;
                   background:rgba(0,0,0,.78); color:#fff; font-size:13px; padding:8px 20px;
                   border-radius:18px; opacity:0; transition:opacity .2s; pointer-events:none; }
          .toast.show { opacity:1; }
          /* 客户端式：底部播放进度条 + 时间 */
          .progress { position:fixed; left:0; right:0; bottom:0; height:5px; z-index:8;
                      background:rgba(255,255,255,.16); cursor:pointer; }
          .progress .fill { height:100%; width:0; background:#00AEEC; box-shadow:0 0 8px rgba(0,174,236,.8); }
          .progress:hover { height:8px; }
          .time { position:fixed; bottom:12px; left:16px; font-size:11px; color:#E6E9EF;
                  text-shadow:0 1px 4px rgba(0,0,0,.8); z-index:8; pointer-events:none; }
          /* 客户端式：画质切换（右下角） */
          .quality { position:fixed; right:22px; bottom:34px; z-index:25; }
          .quality .qbtn { pointer-events:auto; cursor:pointer; border:none; border-radius:14px;
                           background:rgba(22,26,34,.82); color:#E6E9EF; font-size:12px;
                           padding:6px 12px; backdrop-filter:blur(8px); display:flex; align-items:center; gap:6px; }
          .quality .qbtn:hover { background:rgba(40,48,62,.92); }
          .quality .qbtn::after { content:'▾'; font-size:10px; color:#8A93A8; }
          .quality .qmenu { position:absolute; right:0; bottom:40px; display:none; min-width:108px;
                            background:#141823; border:1px solid #232936; border-radius:10px;
                            padding:6px; box-shadow:0 8px 28px rgba(0,0,0,.55); }
          .quality .qmenu.open { display:block; }
          .quality .qitem { padding:8px 12px; border-radius:7px; font-size:12.5px; color:#D6DAE0;
                            cursor:pointer; white-space:nowrap; }
          .quality .qitem:hover { background:rgba(255,255,255,.08); color:#fff; }
          .quality .qitem.cur { color:#00AEEC; font-weight:600; }
          .quality .qitem.cur::after { content:' ✓'; font-size:11px; }
          /* 双击点赞红心动画 */
          .burst { position:fixed; left:50%; top:50%; transform:translate(-50%,-50%) scale(0);
                   font-size:110px; z-index:50; pointer-events:none; opacity:.95;
                   transition:transform .5s ease-out, opacity .5s ease-out; }
          .burst.show { transform:translate(-50%,-50%) scale(1.15); opacity:0; }
          /* 自动播放被拦截时的客户端式播放提示层 */
          .play-hint { position:fixed; left:50%; top:50%; transform:translate(-50%,-50%); display:none;
                       align-items:center; justify-content:center; width:96px; height:96px; border-radius:50%;
                       background:rgba(22,26,34,.72); border:2px solid rgba(255,255,255,.35);
                       cursor:pointer; z-index:30; }
          .play-hint::before { content:'▶'; font-size:38px; color:#fff; margin-left:6px; }
          ::-webkit-scrollbar { width:8px; } ::-webkit-scrollbar-thumb { background:#2A3140; border-radius:4px; }
        </style>
        </head>
        <body>
        <video id="v" playsinline></video>
        <div class="veil"></div>
        <div class="spinner" id="sp"></div>
        <div class="progress" id="prog"><div class="fill" id="fill"></div></div>
        <span class="time" id="tmCur">0:00</span>
        <span class="time" id="tmDur" style="left:auto;right:16px">0:00</span>
        <div class="play-hint" id="playHint"></div>

        <div class="top">
          <button class="btn" id="btnLogin" onclick="send('login')">登录 B 站</button>
        </div>

        <div class="quality" id="qualBox">
          <button class="qbtn" id="qualBtn" onclick="toggleQual()">画质</button>
          <div class="qmenu" id="qualMenu">
            <div class="qitem" data-q="64" onclick="setQual(64,'1080P')">1080P</div>
            <div class="qitem" data-q="48" onclick="setQual(48,'720P')">720P</div>
            <div class="qitem" data-q="32" onclick="setQual(32,'480P')">480P</div>
            <div class="qitem" data-q="16" onclick="setQual(16,'360P')">360P</div>
          </div>
        </div>

        <div class="rail">
          <div class="act" id="aLike" onclick="send('like')">
            <span class="ic">👍</span><span class="cnt" id="cLike">0</span>
          </div>
          <div class="act" id="aCoin" onclick="send('coin')">
            <span class="ic">🪙</span><span class="cnt" id="cCoin">投币</span>
          </div>
          <div class="act" id="aCollect" onclick="send('collect')">
            <span class="ic">⭐</span><span class="cnt" id="cCollect">收藏</span>
          </div>
          <div class="act" id="aCmt" onclick="send('comments')">
            <span class="ic">💬</span><span class="cnt" id="cCmt">0</span>
          </div>
          <div class="act" onclick="send('random')">
            <span class="ic">🔀</span><span class="cnt">随机</span>
          </div>
        </div>

        <div class="info">
          <div class="title" id="title">—</div>
          <div class="author">
            <span class="avatar" id="avatar">?</span>
            <span class="name" id="author" onclick="send('open_author')">—</span>
            <button class="btn follow" id="btnFollow" onclick="send('follow')">关注</button>
          </div>
          <div class="desc" id="desc"></div>
          <div class="stats" id="stats"></div>
        </div>

        <div class="cmt-panel" id="cmtPanel">
          <div class="cmt-head"><b id="cmtTitle">评论</b><button class="cmt-close" onclick="closeCmt()">✕</button></div>
          <div class="cmt-list" id="cmtList"></div>
          <div class="cmt-input">
            <input id="cmtInput" placeholder="发一条友善的评论…" maxlength="500">
            <button id="btnSend" onclick="sendCmt()">发送</button>
          </div>
        </div>

        <div class="toast" id="toast"></div>

        <script>
          var cur = null, followState = 0, likeState = 0, colState = 0, coinState = 0, wheelAcc = 0;
          var QUAL_NAMES = {64:'1080P', 48:'720P', 32:'480P', 16:'360P'};
          var curQuals = {}, curQ = 0;
          function post(m){ window.chrome.webview.postMessage(JSON.stringify(m)); }
          function send(t){ post({type:t, aid:cur?cur.aid:0, uid:cur?cur.authorUid:0, bvid:cur?cur.bvid:""}); }
          window.__play = function (v) {
            cur = v;
            likeState = 0; colState = 0; followState = 0; coinState = 0;
            document.getElementById('sp').style.display = 'none';
            document.getElementById('title').textContent = v.title || '—';
            document.getElementById('author').textContent = v.author || '未知UP主';
            document.getElementById('avatar').textContent = (v.author || '?').trim().charAt(0);
            document.getElementById('desc').textContent = v.desc || '';
            var st = document.getElementById('stats');
            st.textContent = window.__fmt(v.view) + '播放 · ' + window.__fmt(v.like) + '赞 · ' + window.__fmt(v.danmaku) + '弹幕';
            document.getElementById('cLike').textContent = window.__fmt(v.like);
            document.getElementById('cCmt').textContent = v.danmaku > 0 ? window.__fmt(v.danmaku) : '0';
            var f = document.getElementById('btnFollow');
            f.textContent = '关注'; f.classList.remove('done');
            var aL = document.getElementById('aLike'); aL.classList.remove('on');
            var aC = document.getElementById('aCollect'); aC.classList.remove('on');
            var aCo = document.getElementById('aCoin'); aCo.classList.remove('on'); document.getElementById('cCoin').textContent = '投币';
            var vd = document.getElementById('v');
            // 纯客户端播放：不使用任何 B 站网页播放器。直连地址失效时应用内提示并自动跳到下一个
            vd.onerror = function(){
              window.__toast('该视频暂时无法播放，自动跳过');
              post({type:'video_error'});
            };
            vd.onended = function(){ post({type:'next'}); };   // 播完自动连播下一个（客户端式刷视频）
            if (!v.url) { window.__toast('该视频暂时无法播放，自动跳过'); post({type:'video_error'}); return; }
            document.getElementById('playHint').style.display = 'none';
            curQuals = v.urls || {};
            var qs = [64,48,32,16].filter(function(q){ return !!curQuals[q]; });
            curQ = qs.length ? qs[0] : 0;
            refreshQual();
            vd.src = v.url;
            vd.muted = false;
            var pr = vd.play();
            if (pr && pr.catch) pr.catch(function(){
              // 严格自动播放策略下带声音会被拦截：先静音播放，点击视频即可开启声音
              vd.muted = true;
              var pr2 = vd.play();
              if (pr2 && pr2.catch) pr2.catch(function(){
                // 静音也被拦截：显示客户端式播放按钮，点击后带声播放
                document.getElementById('playHint').style.display = 'flex';
                return;
              });
              window.__toast('已静音播放，点击视频可开启声音');
            });
            closeCmt();
          };
          window.__loading = function(){ document.getElementById('sp').style.display = 'block'; };
          window.__login = function(ok, uname){
            var b = document.getElementById('btnLogin');
            if (ok) {
              b.innerHTML = '<span class="login-ok">👤 ' + uname + '</span>';
              b.onclick = function(){ post({type:'login'}); };
            } else {
              b.textContent = '登录 B 站';
            }
          };
          window.__follow = function(done){
            followState = done ? 1 : 0;
            var f = document.getElementById('btnFollow');
            if (done) { f.textContent = '已关注'; f.classList.add('done'); } else { f.textContent = '关注'; f.classList.remove('done'); }
          };
          window.__like = function(on, cnt){
            likeState = on ? 1 : 0;
            document.getElementById('aLike').classList.toggle('on', !!on);
            if (cnt !== undefined) document.getElementById('cLike').textContent = window.__fmt(cnt);
          };
          window.__coin = function(on){
            coinState = on ? 1 : 0;
            document.getElementById('aCoin').classList.toggle('on', !!on);
            if (on) document.getElementById('cCoin').textContent = '已投';
          };
          window.__collect = function(on){
            colState = on ? 1 : 0;
            document.getElementById('aCollect').classList.toggle('on', !!on);
          };
          window.__comments = function(list){
            var box = document.getElementById('cmtList'); box.innerHTML = '';
            document.getElementById('cmtTitle').textContent = '评论 (' + (list ? list.length : 0) + ')';
            if (!list || !list.length) {
              box.innerHTML = '<div style="color:#6B7280;font-size:12.5px;padding:16px 0">暂无评论，来抢沙发～</div>';
            } else {
              for (var i = 0; i < list.length; i++) {
                var x = list[i], d = document.createElement('div'); d.className = 'cm';
                d.innerHTML = '<div class="u"></div><div class="m"></div><div class="meta"></div>';
                d.querySelector('.u').textContent = x.user;
                d.querySelector('.m').textContent = x.message;
                d.querySelector('.meta').textContent = x.like + ' 赞 · ' + x.time;
                box.appendChild(d);
              }
            }
            document.getElementById('cmtPanel').classList.add('open');
          };
          window.__toast = function(msg){
            var t = document.getElementById('toast'); t.textContent = msg; t.classList.add('show');
            clearTimeout(window.__toastT); window.__toastT = setTimeout(function(){ t.classList.remove('show'); }, 2200);
          };
          window.__fmt = function (n) {
            if (n >= 100000000) return (n / 100000000).toFixed(1) + '亿';
            if (n >= 10000) return (n / 10000).toFixed(1) + '万';
            return '' + n;
          };
          function fmtTime(s){
            s = Math.floor(s || 0);
            var m = Math.floor(s / 60), ss = s % 60;
            return m + ':' + (ss < 10 ? '0' : '') + ss;
          }
          // 客户端式画质切换：当前视频可用清晰度（C# 侧一次取全），点击即时切换并续播
          function refreshQual(){
            var qs = [64,48,32,16].filter(function(q){ return !!curQuals[q]; });
            if (curQ !== 0 && !curQuals[curQ]) curQ = 0;
            document.getElementById('qualBtn').textContent = curQ ? (QUAL_NAMES[curQ] || ('P' + curQ)) : '画质';
            var items = document.querySelectorAll('.qitem');
            for (var i = 0; i < items.length; i++) {
              var q = parseInt(items[i].getAttribute('data-q'), 10);
              items[i].style.display = curQuals[q] ? 'block' : 'none';
              items[i].classList.toggle('cur', q === curQ);
            }
          }
          function toggleQual(){ document.getElementById('qualMenu').classList.toggle('open'); }
          function setQual(q, name){
            document.getElementById('qualMenu').classList.remove('open');
            if (!curQuals[q] || q === curQ) return;
            var wasPaused = vd.paused, wasMuted = vd.muted, t = vd.currentTime;
            curQ = q;
            vd.src = curQuals[q];
            vd.muted = wasMuted;
            if (vd.readyState) try { vd.currentTime = t; } catch (e) {}
            refreshQual();
            if (!wasPaused) { var pr = vd.play(); if (pr && pr.catch) pr.catch(function(){}); }
          }
          // 客户端式进度条：播放中实时更新，点击跳转
          var vd = document.getElementById('v'), prog = document.getElementById('prog'), fill = document.getElementById('fill');
          function updateProgress(){
            if (vd.duration) fill.style.width = (vd.currentTime / vd.duration * 100) + '%';
            document.getElementById('tmCur').textContent = fmtTime(vd.currentTime);
            document.getElementById('tmDur').textContent = fmtTime(vd.duration);
          }
          vd.addEventListener('timeupdate', updateProgress);
          setInterval(updateProgress, 500);   // 兜底轮询，确保进度条始终推进
          prog.addEventListener('click', function(e){
            if (!vd.duration) return;
            var r = prog.getBoundingClientRect();
            vd.currentTime = (e.clientX - r.left) / r.width * vd.duration;
          });
          // 双击点赞（客户端经典交互）：红心动画 + 调用点赞接口
          vd.addEventListener('dblclick', function(){
            var b = document.createElement('div');
            b.className = 'burst'; b.textContent = '❤️';
            document.body.appendChild(b);
            requestAnimationFrame(function(){ b.classList.add('show'); });
            setTimeout(function(){ b.remove(); }, 520);
            post({type:'like', aid:cur?cur.aid:0});
          });
          function closeCmt(){ document.getElementById('cmtPanel').classList.remove('open'); }
          function sendCmt(){
            var m = document.getElementById('cmtInput').value.trim();
            if (!m) return;
            post({type:'send_comment', aid:cur?cur.aid:0, message:m});
            document.getElementById('cmtInput').value = '';
          }
          document.addEventListener('wheel', function(e){
            wheelAcc += e.deltaY;
            if (wheelAcc > 90) { wheelAcc = 0; post({type:'next'}); }
            else if (wheelAcc < -90) { wheelAcc = 0; post({type:'prev'}); }
          }, { passive:true });
          document.addEventListener('keydown', function(e){
            if (e.key === 'ArrowDown') post({type:'next'});
            else if (e.key === 'ArrowUp') post({type:'prev'});
            else if (e.key === 'Escape') closeCmt();
          });
          document.getElementById('v').addEventListener('click', function(){
            var hint = document.getElementById('playHint');
            if (hint.style.display !== 'none') { hint.style.display = 'none'; vd.muted = false; vd.play().catch(function(){}); return; }
            if (vd.muted) { vd.muted = false; vd.play().catch(function(){}); return; }
            if (vd.paused) vd.play().catch(function(){}); else vd.pause();
          });
          document.getElementById('playHint').addEventListener('click', function(){
            document.getElementById('playHint').style.display = 'none';
            vd.muted = false; vd.play().catch(function(){});
          });
          document.addEventListener('click', function(e){
            var qb = document.getElementById('qualBox');
            var qm = document.getElementById('qualMenu');
            if (qm.classList.contains('open') && !qb.contains(e.target)) qm.classList.remove('open');
            if (e.target === document.getElementById('cmtPanel')) return;
            var p = document.getElementById('cmtPanel');
            if (p.classList.contains('open') && !p.contains(e.target)) closeCmt();
          });
        </script>
        </body>
        </html>
        """;

    /// <summary>生成播放 JS 脚本（全屏切换）；urls 为全部可用清晰度 qn -> 直链</summary>
    public static string BuildPlay(BiliVideo v, string? url, Dictionary<int, string>? urls = null)
    {
        var sb = new StringBuilder("window.__play({");
        sb.Append("bvid:").Append(JsonSerializer.Serialize(v.Bvid))
          .Append(",aid:").Append(v.Aid)
          .Append(",cid:").Append(v.Cid)
          .Append(",title:").Append(JsonSerializer.Serialize(v.Title))
          .Append(",author:").Append(JsonSerializer.Serialize(v.Author))
          .Append(",authorUid:").Append(v.AuthorUid)
          .Append(",view:").Append(v.View)
          .Append(",like:").Append(v.Like)
          .Append(",danmaku:").Append(v.Danmaku)
          .Append(",desc:").Append(JsonSerializer.Serialize(v.Desc))
          .Append(",url:").Append(JsonSerializer.Serialize(url ?? ""));
        sb.Append(",urls:");
        if (urls is { Count: > 0 })
        {
            sb.Append("{");
            var first = true;
            foreach (var (q, u) in urls)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(q).Append(':').Append(JsonSerializer.Serialize(u));
            }
            sb.Append("}");
        }
        else
        {
            sb.Append("{}");
        }
        sb.Append("});");
        return sb.ToString();
    }

    public static string BuildLoading() => "window.__loading();";

    public static string BuildLogin(bool ok, string uname)
    {
        var sb = new StringBuilder("window.__login(");
        sb.Append(ok ? "true" : "false").Append(",").Append(JsonSerializer.Serialize(uname)).Append(");");
        return sb.ToString();
    }

    public static string BuildFollow(bool done) => $"window.__follow({(done ? "true" : "false")});";

    public static string BuildLike(bool on, long cnt) => $"window.__like({(on ? "true" : "false")},{cnt});";

    public static string BuildCollect(bool on) => $"window.__collect({(on ? "true" : "false")});";

    public static string BuildCoin(bool on) => $"window.__coin({(on ? "true" : "false")});";

    public static string BuildComments(List<BiliComment> list)
    {
        var sb = new StringBuilder("window.__comments([");
        foreach (var c in list)
        {
            sb.Append("{user:").Append(JsonSerializer.Serialize(c.User))
              .Append(",message:").Append(JsonSerializer.Serialize(c.Message))
              .Append(",like:").Append(c.Like)
              .Append(",time:").Append(JsonSerializer.Serialize(c.Time))
              .Append("},");
        }
        sb.Append("]);");
        return sb.ToString();
    }

    public static string BuildToast(string msg)
    {
        return $"window.__toast({JsonSerializer.Serialize(msg)});";
    }
}

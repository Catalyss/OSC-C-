using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore;
using Newtonsoft.Json.Linq;
using System.Net.Sockets;
using CoreOSC.IO;
using CoreOSC;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using System.Net.Http.Headers;
using static LogProcessor;
using System.Xml;

namespace BlocklyHandler
{
    class Blockly
    {
        #region Config & State Variables
        private static int webPort = 80;
        private static int oscPortOut = 9002;
        private static int oscPortIn = 9003;
        private static int saveCount = 5;
        private static string CurrentID = "none";
        private static IWebHost webHost;
        private static string PUBIP = "localhost";
        private static string DNS = "";
        private static string httpsTXTReplace = "http://";
        private static string wsTXTReplace = "ws://";
        private static bool UseHTTPS = false;
        private static bool logger;

        #endregion

        #region HTML Templates

        private static string GetIndexHtmlTemplate()
        {
            string wsProtocol = UseHTTPS ? "wss://" : "ws://";
            string wsUrl = string.IsNullOrEmpty(DNS) ? $"{wsProtocol}{PUBIP}" : $"{wsProtocol}{DNS}";
            if ((webPort != 80 && !UseHTTPS) || (webPort != 443 && UseHTTPS))
                wsUrl += $":{webPort}";

            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Blockly Visual Programming</title>
    <script src=""https://unpkg.com/blockly/blockly.min.js""></script>
    <style>
        body { 
            margin: 0; 
            padding: 0; 
            font-family: 'Segoe UI', Arial, sans-serif;
            background: #1a1a1a;
            color: #e0e0e0;
            display: flex;
            flex-direction: column;
            height: 100vh;
        }
        
        #header {
            background: #121212;
            padding: 12px 20px;
            border-bottom: 2px solid #333;
            display: flex;
            justify-content: space-between;
            align-items: center;
        }
        
        #header h1 {
            margin: 0;
            font-size: 1.5rem;
        }
        
        #controls {
            display: flex;
            gap: 10px;
        }
        
        button {
            padding: 8px 16px;
            border: none;
            border-radius: 6px;
            cursor: pointer;
            font-size: 0.9rem;
            transition: background 0.2s;
        }
        
        button.primary {
            background: #4caf50;
            color: #061206;
        }
        
        button.primary:hover {
            background: #45a049;
        }
        
        button.secondary {
            background: #333;
            color: #e0e0e0;
        }
        
        button.secondary:hover {
            background: #444;
        }
        
        #main-container {
            display: flex;
            flex: 1;
            overflow: hidden;
        }
        
        #blocklyDiv {
            flex: 1;
            height: 100%;
        }
        
        #code-panel {
            width: 400px;
            background: #0d0d0d;
            border-left: 2px solid #333;
            display: flex;
            flex-direction: column;
            overflow: hidden;
        }
        
        #code-tabs {
            display: flex;
            background: #121212;
            border-bottom: 1px solid #333;
        }
        
        .code-tab {
            padding: 10px 20px;
            cursor: pointer;
            border-bottom: 2px solid transparent;
        }
        
        .code-tab.active {
            border-bottom-color: #4caf50;
            background: #0d0d0d;
        }
        
        #code-content {
            flex: 1;
            overflow: auto;
            padding: 15px;
        }
        
        #generatedCode, #output {
            background: #000;
            border: 1px solid #333;
            border-radius: 4px;
            padding: 12px;
            font-family: 'Consolas', 'Monaco', monospace;
            font-size: 0.9rem;
            white-space: pre-wrap;
            word-wrap: break-word;
            min-height: 200px;
            display: none;
        }
        
        #generatedCode.active, #output.active {
            display: block;
        }
        
        #status {
            padding: 8px 15px;
            background: #121212;
            border-top: 1px solid #333;
            font-size: 0.85rem;
            color: #999;
        }
        
        .status-connected { color: #4caf50; }
        .status-disconnected { color: #f44336; }
    </style>
</head>
<body>
    <div id=""header"">
        <h1>🧩 Blockly Visual Programming</h1>
        <div id=""controls"">
            <button class=""secondary"" onclick=""loadWorkspace()"">Load</button>
            <button class=""secondary"" onclick=""saveWorkspace()"">Save</button>
            <button class=""secondary"" onclick=""clearWorkspace()"">Clear</button>
            <button class=""primary"" onclick=""runCode()"">▶ Run Code</button>
        </div>
    </div>
    
    <div id=""main-container"">
        <div id=""blocklyDiv""></div>
        
        <div id=""code-panel"">
            <div id=""code-tabs"">
                <div class=""code-tab active"" onclick=""switchTab('code')"">Generated Code</div>
                <div class=""code-tab"" onclick=""switchTab('output')"">Output</div>
            </div>
            <div id=""code-content"">
                <pre id=""generatedCode"" class=""active""></pre>
                <pre id=""output""></pre>
            </div>
        </div>
    </div>
    
    <div id=""status"">
        <span id=""ws-status"" class=""status-disconnected"">⚫ Disconnected</span>
    </div>

    <script>
        // WebSocket connection
        const ws = new WebSocket('" + wsUrl + @"/ws');
        let workspace;
        
        ws.onopen = () => {
            console.log('WebSocket connected');
            document.getElementById('ws-status').innerHTML = '🟢 Connected';
            document.getElementById('ws-status').className = 'status-connected';
        };
        
        ws.onclose = () => {
            console.log('WebSocket disconnected');
            document.getElementById('ws-status').innerHTML = '⚫ Disconnected';
            document.getElementById('ws-status').className = 'status-disconnected';
        };
        
        ws.onmessage = (event) => {
            try {
                const data = JSON.parse(event.data);
                console.log('Received:', data);
                
                if (data.type === 'execution_result') {
                    document.getElementById('output').textContent = data.output || data.error || 'No output';
                    switchTab('output');
                }
            } catch (err) {
                console.error('Parse error:', err);
            }
        };
        
        // Initialize Blockly
        const toolbox = {
            'kind': 'categoryToolbox',
            'contents': [
                {
                    'kind': 'category',
                    'name': 'Logic',
                    'colour': '#5C81A6',
                    'contents': [
                        {'kind': 'block', 'type': 'controls_if'},
                        {'kind': 'block', 'type': 'logic_compare'},
                        {'kind': 'block', 'type': 'logic_operation'},
                        {'kind': 'block', 'type': 'logic_negate'},
                        {'kind': 'block', 'type': 'logic_boolean'},
                        {'kind': 'block', 'type': 'logic_null'},
                        {'kind': 'block', 'type': 'logic_ternary'}
                    ]
                },
                {
                    'kind': 'category',
                    'name': 'Loops',
                    'colour': '#5CA65C',
                    'contents': [
                        {'kind': 'block', 'type': 'controls_repeat_ext'},
                        {'kind': 'block', 'type': 'controls_whileUntil'},
                        {'kind': 'block', 'type': 'controls_for'},
                        {'kind': 'block', 'type': 'controls_forEach'},
                        {'kind': 'block', 'type': 'controls_flow_statements'}
                    ]
                },
                {
                    'kind': 'category',
                    'name': 'Math',
                    'colour': '#5C68A6',
                    'contents': [
                        {'kind': 'block', 'type': 'math_number'},
                        {'kind': 'block', 'type': 'math_arithmetic'},
                        {'kind': 'block', 'type': 'math_single'},
                        {'kind': 'block', 'type': 'math_trig'},
                        {'kind': 'block', 'type': 'math_constant'},
                        {'kind': 'block', 'type': 'math_number_property'},
                        {'kind': 'block', 'type': 'math_round'},
                        {'kind': 'block', 'type': 'math_modulo'},
                        {'kind': 'block', 'type': 'math_random_int'},
                        {'kind': 'block', 'type': 'math_random_float'}
                    ]
                },
                {
                    'kind': 'category',
                    'name': 'Text',
                    'colour': '#5CA68D',
                    'contents': [
                        {'kind': 'block', 'type': 'text'},
                        {'kind': 'block', 'type': 'text_join'},
                        {'kind': 'block', 'type': 'text_append'},
                        {'kind': 'block', 'type': 'text_length'},
                        {'kind': 'block', 'type': 'text_isEmpty'},
                        {'kind': 'block', 'type': 'text_indexOf'},
                        {'kind': 'block', 'type': 'text_charAt'},
                        {'kind': 'block', 'type': 'text_getSubstring'},
                        {'kind': 'block', 'type': 'text_changeCase'},
                        {'kind': 'block', 'type': 'text_trim'},
                        {'kind': 'block', 'type': 'text_print'}
                    ]
                },
                {
                    'kind': 'category',
                    'name': 'Lists',
                    'colour': '#745CA6',
                    'contents': [
                        {'kind': 'block', 'type': 'lists_create_with'},
                        {'kind': 'block', 'type': 'lists_create_empty'},
                        {'kind': 'block', 'type': 'lists_repeat'},
                        {'kind': 'block', 'type': 'lists_length'},
                        {'kind': 'block', 'type': 'lists_isEmpty'},
                        {'kind': 'block', 'type': 'lists_indexOf'},
                        {'kind': 'block', 'type': 'lists_getIndex'},
                        {'kind': 'block', 'type': 'lists_setIndex'}
                    ]
                },
                {
                    'kind': 'category',
                    'name': 'Variables',
                    'colour': '#A65C81',
                    'custom': 'VARIABLE'
                },
                {
                    'kind': 'category',
                    'name': 'Functions',
                    'colour': '#9A5CA6',
                    'custom': 'PROCEDURE'
                }
            ]
        };
        
        workspace = Blockly.inject('blocklyDiv', {
            toolbox: toolbox,
            grid: {
                spacing: 20,
                length: 3,
                colour: '#2a2a2a',
                snap: true
            },
            zoom: {
                controls: true,
                wheel: true,
                startScale: 1.0,
                maxScale: 3,
                minScale: 0.3,
                scaleSpeed: 1.2
            },
            trashcan: true,
            theme: Blockly.Theme.defineTheme('dark', {
                'base': Blockly.Themes.Classic,
                'componentStyles': {
                    'workspaceBackgroundColour': '#1a1a1a',
                    'toolboxBackgroundColour': '#0d0d0d',
                    'flyoutBackgroundColour': '#121212',
                    'flyoutForegroundColour': '#ccc',
                    'flyoutOpacity': 0.9,
                    'scrollbarColour': '#797979',
                    'insertionMarkerColour': '#fff',
                    'insertionMarkerOpacity': 0.3,
                    'scrollbarOpacity': 0.4,
                    'cursorColour': '#d0d0d0'
                }
            })
        });
        
        // Update code display on workspace change
        workspace.addChangeListener(() => {
            const code = Blockly.JavaScript.workspaceToCode(workspace);
            document.getElementById('generatedCode').textContent = code || '// No blocks yet';
        });
        
        function switchTab(tab) {
            document.querySelectorAll('.code-tab').forEach(t => t.classList.remove('active'));
            document.querySelectorAll('#code-content > pre').forEach(p => p.classList.remove('active'));
            
            if (tab === 'code') {
                document.querySelectorAll('.code-tab')[0].classList.add('active');
                document.getElementById('generatedCode').classList.add('active');
            } else {
                document.querySelectorAll('.code-tab')[1].classList.add('active');
                document.getElementById('output').classList.add('active');
            }
        }
        
        function runCode() {
            const code = Blockly.JavaScript.workspaceToCode(workspace);
            if (!code.trim()) {
                alert('No code to run! Add some blocks first.');
                return;
            }
            
            ws.send(JSON.stringify({
                action: 'run_code',
                language: 'javascript',
                code: code
            }));
            
            document.getElementById('output').textContent = 'Running code...';
            switchTab('output');
        }
        
        function saveWorkspace() {
            const xml = Blockly.Xml.workspaceToDom(workspace);
            const xmlText = Blockly.Xml.domToText(xml);
            
            ws.send(JSON.stringify({
                action: 'save_workspace',
                workspace: xmlText
            }));
            
            // Also save locally
            localStorage.setItem('blockly_workspace', xmlText);
            alert('Workspace saved!');
        }
        
        function loadWorkspace() {
            const xmlText = localStorage.getItem('blockly_workspace');
            if (xmlText) {
                const xml = Blockly.utils.xml.textToDom(xmlText);
                workspace.clear();
                Blockly.Xml.domToWorkspace(xml, workspace);
                alert('Workspace loaded!');
            } else {
                alert('No saved workspace found.');
            }
        }
        
        function clearWorkspace() {
            if (confirm('Clear all blocks?')) {
                workspace.clear();
            }
        }
        
        // Auto-load workspace on page load
        window.addEventListener('load', () => {
            const saved = localStorage.getItem('blockly_workspace');
            if (saved) {
                try {
                    const xml = Blockly.utils.xml.textToDom(saved);
                    Blockly.Xml.domToWorkspace(xml, workspace);
                } catch (e) {
                    console.error('Failed to load saved workspace:', e);
                }
            }
        });
    </script>
</body>
</html>";
        }
        
        #endregion

        #region Main Entry
        public static async Task BLMain(string[] args)
        {
            ParseArguments(args);
            await ConfigureHTTPSIfNeeded();
            await FetchPublicIP();
            PrintServerAddress();

            StartWebServer();
        }
        #endregion

        #region HTTPS & Network Helpers
        private static async Task ConfigureHTTPSIfNeeded()
        {
            if (!UseHTTPS) return;

            string certFilePath = "./cert/certificate.crt";
            string keyFilePath = "./cert/private.key";
            string pfxFilePath = "./cert/certificate.pfx";
            string password = "your_password";
            GeneratePfx(certFilePath, keyFilePath, pfxFilePath, password);
        }

        public static void GeneratePfx(string certFilePath, string keyFilePath, string pfxFilePath, string password)
        {
            try
            {
                if (!File.Exists(certFilePath) || !File.Exists(keyFilePath))
                {
                    Console.WriteLine("Certificate files not found, skipping PFX generation");
                    return;
                }

                var cert = new X509Certificate2(certFilePath);
                string privateKeyText = File.ReadAllText(keyFilePath);

                using (RSA rsa = RSA.Create())
                {
                    rsa.ImportFromPem(privateKeyText.ToCharArray());
                    var pfx = new X509Certificate2Collection(cert);
                    pfx[0] = pfx[0].CopyWithPrivateKey(rsa);
                    File.WriteAllBytes(pfxFilePath, pfx.Export(X509ContentType.Pfx, password));
                    Console.WriteLine($"PFX file created: {pfxFilePath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating PFX: {ex.Message}");
            }
        }

        private static async Task FetchPublicIP()
        {
            try
            {
                using HttpClient client = new HttpClient();
                PUBIP = await client.GetStringAsync("https://api.ipify.org");
            }
            catch { PUBIP = "localhost"; }
        }

        private static void PrintServerAddress()
        {
            string ip = DNS = string.IsNullOrEmpty(DNS) ? PUBIP : DNS;
            string portSuffix = (webPort != 80 && !UseHTTPS) && (webPort != 443 && UseHTTPS) ? $":{webPort}" : "";
            Console.WriteLine($"Your public IP address is: {ip}{portSuffix}");
        }

        private static bool IsLocalhostRequest(HttpContext context) =>
            context.Connection.RemoteIpAddress.ToString() == "::1" ||
            context.Connection.RemoteIpAddress.ToString() == "::ffff:127.0.0.1" ||
            context.Connection.RemoteIpAddress.ToString() == $"::ffff:{PUBIP}" ||
            context.Connection.RemoteIpAddress.ToString() == PUBIP;
        #endregion

        #region Web Server
        private static void StartWebServer()
        {
            var builder = WebHost.CreateDefaultBuilder()
                .ConfigureKestrel(options =>
                {
                  options.Limits.MaxRequestBodySize = null;
                  if (File.Exists("./cert/certificate.pfx"))
                  {
                    options.ListenAnyIP(443, listenOptions =>
                    {
                      listenOptions.UseHttps("./cert/certificate.pfx", "your_password");
                    });
                  }
                  options.ListenAnyIP(webPort);
                })
                .ConfigureServices(services => services.AddRouting())
                .Configure(app =>
                {
                    app.UseWebSockets();
                    app.UseRouting();

                    app.Use(async (context, next) =>
                    {
                        if (context.Request.Path == "/ws" || context.Request.Path == "/" + DNS + "/ws")
                        {
                            if (context.WebSockets.IsWebSocketRequest)
                            {
                                var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                                var handler = new WebSocketHandler(webSocket);
                                await handler.HandleAsync();
                            }
                            else
                            {
                                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                            }
                        }
                        else
                        {
                            await next();
                        }
                    });

                    app.UseEndpoints(endpoints =>
                    {
                        MapWebEndpoints(endpoints);
                    });
                });

            if (UseHTTPS)
                builder.UseUrls($"https://*:{webPort}");
            else
                builder.UseUrls($"http://*:{webPort}");

            webHost = builder.Build();
            webHost.Run();
        }

        private static void MapWebEndpoints(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/", async context =>
            {
                Console.WriteLine($"Connection from: {context.Connection.RemoteIpAddress}");
                string html = GetIndexHtmlTemplate();
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(html);
            });
        }

        private static string ToUnicodeEscape(string s)
        {
            if (s == null) return "";

            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c <= 127)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append("\\u");
                    sb.Append(((int)c).ToString("X4"));
                }
            }
            return sb.ToString();
        }
        #endregion

        #region HTML & File Helpers

        public static bool WildcardMatch(string input, string pattern)
        {
            string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(input, regexPattern);
        }

        #endregion

        #region Argument Parsing
        private static void ParseArguments(string[] args)
        {
            string lastArg = "";
            foreach (var arg in args)
            {
                if (lastArg.StartsWith("-"))
                {
                    switch (lastArg)
                    {
                        case "-WebPort": webPort = int.Parse(arg); break;
                        case "-OSCPortOUT": oscPortOut = int.Parse(arg); break;
                        case "-OSCPortIN": oscPortIn = int.Parse(arg); break;
                        case "-DNS": DNS = arg; break;
                        case "-slot": saveCount = int.Parse(arg); break;
                        case "-logger": logger = bool.Parse(arg); break;
                        case "-usehttps": UseHTTPS = true; httpsTXTReplace = "https://"; wsTXTReplace = "wss://"; break;
                    }
                }
                lastArg = arg;
            }
        }
        #endregion
    }

    #region WebSocket Handler

    public class WebSocketHandler
    {
        private static readonly List<WebSocket> _webSockets = new List<WebSocket>();
        private readonly WebSocket _webSocket;

        public WebSocketHandler(WebSocket webSocket)
        {
            _webSocket = webSocket;
            lock (_webSockets) { _webSockets.Add(webSocket); }
        }

        public async Task HandleAsync()
        {
            var buffer = new byte[1024 * 8];
            WebSocketReceiveResult result = null;

            try
            {
                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await ProcessMessage(msg, _webSocket);
                    }
                } while (!result.CloseStatus.HasValue);
            }
            finally
            {
                lock (_webSockets) { _webSockets.Remove(_webSocket); }
                if (_webSocket.State != WebSocketState.Closed)
                    await _webSocket.CloseAsync(result?.CloseStatus ?? WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
        }

        private static async Task ProcessMessage(string json, WebSocket socket)
        {
            try
            {
                var jObj = JObject.Parse(json);
                var action = jObj["action"]?.ToString();

                switch (action)
                {
                    case "run_code":
                        await HandleRunCode(jObj, socket);
                        break;
                    
                    case "save_workspace":
                        await HandleSaveWorkspace(jObj, socket);
                        break;
                    
                    case "nothing":
                        break;
                    
                    default:
                        Console.WriteLine("Unknown action: " + action);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error processing WebSocket message: " + ex);
            }
        }

        private static async Task HandleRunCode(JObject jObj, WebSocket socket)
        {
            var code = jObj["code"]?.ToString();
            var language = jObj["language"]?.ToString() ?? "javascript";
            
            Console.WriteLine($"\n=== Executing {language} Code ===");
            Console.WriteLine(code);
            Console.WriteLine("================================\n");

            // For now, just echo back that we received it
            // You could use Jint or ClearScript to actually execute JavaScript
            var response = new
            {
                type = "execution_result",
                output = $"Code received! ({code?.Length ?? 0} characters)\n\nTo actually execute JavaScript, you'll need to add a JS interpreter like Jint or ClearScript.",
                success = true
            };

            await SendMessage(socket, JsonConvert.SerializeObject(response));
        }

        private static async Task HandleSaveWorkspace(JObject jObj, WebSocket socket)
        {
            var workspace = jObj["workspace"]?.ToString();
            
            if (!string.IsNullOrEmpty(workspace))
            {
                var savePath = "./workspaces";
                Directory.CreateDirectory(savePath);
                
                var filename = $"{savePath}/workspace_{DateTime.Now:yyyyMMdd_HHmmss}.xml";
                await File.WriteAllTextAsync(filename, workspace);
                
                Console.WriteLine($"Workspace saved to: {filename}");
                
                var response = new
                {
                    type = "save_result",
                    success = true,
                    filename = filename
                };
                
                await SendMessage(socket, JsonConvert.SerializeObject(response));
            }
        }

        private static async Task SendMessage(WebSocket socket, string message)
        {
            if (socket.State == WebSocketState.Open)
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        public static async Task BroadcastMessage(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            var segment = new ArraySegment<byte>(buffer);

            List<WebSocket> disconnected = new();

            lock (_webSockets)
            {
                foreach (var ws in _webSockets)
                {
                    if (ws.State == WebSocketState.Open)
                    {
                        try
                        {
                            ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None).Wait();
                        }
                        catch
                        {
                            disconnected.Add(ws);
                        }
                    }
                    else
                    {
                        disconnected.Add(ws);
                    }
                }

                foreach (var ws in disconnected)
                    _webSockets.Remove(ws);
            }
        }
    }
    #endregion
}
// Current Page for the HTML generation is deprecated
/* 
private static string GetIndexHtml(bool isHost, string currentID = "none")
        {
            var settingsButton = isHost ? $@"<button onclick=""location.href='/settings'"">Setting page</button>
                                             <button onclick=""location.href='/routing'"">Routing page</button>
                                             <button onclick=""location.href='/parameter'"">Parameters page</button>
                                             <button onclick='openScripting()'>Open Scripting</button>" : "";
            var injectAvatarToggle = "";
            if (currentID != "none")
            {
                injectAvatarToggle = UpdateHtml(currentID, isHost);
            }

            return @"<!DOCTYPE html>
                <html lang=""en"">
                <head>
                <meta charset=""UTF-8"">
                <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
                <style>
                    body { background-color: #121212; color: #e0e0e0; font-family: 'Segoe UI', Arial, sans-serif; padding: 12px; }
                    h1 { margin: 8px 0 12px 0; }
                    .top-bar { display:flex; gap:12px; align-items:center; flex-wrap:wrap; margin-bottom:12px; }
                    .controls { display:flex; gap:8px; align-items:center; margin: 10px 0; }
                    .controls button { padding:6px 10px; border-radius:6px; border:none; cursor:pointer; background:#333; color:#e0e0e0; }
                    .controls button.active { background:#4caf50; color:#061206; }
                    #parameters-grid {display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 14px; align-items: start;}
                    .parameter-container { border:1px solid #333; padding:10px; border-radius:8px; background:#1a1a1a; }
                    .side-panel { position: fixed; left: 0; top: 0; width: 180px; height: 100%; background: #1a1a1a; border-right: 1px solid #333; padding: 10px; display: flex; flex-direction: column; gap: 8px; overflow-y: auto; }
                    .side-panel button { padding: 6px 8px; border-radius: 6px; border: none; background: #333; color: #e0e0e0; cursor: pointer; font-size: 0.9rem; }
                    .side-panel button.active { background: #4caf50; color: #061206; }
                    .button-container { display: flex; gap: 8px; }
                    
                    #scripting-modal { 
                        position: fixed; 
                        top: 0; 
                        left: 0; 
                        width: 100%; 
                        height: 100%; 
                        background: rgba(0,0,0,0.9); 
                        z-index: 1000; 
                        display: none; 
                        align-items: center; 
                        justify-content: center; 
                    }
                    #scripting-content { 
                        background: #1a1a1a; 
                        border-radius: 10px; 
                        width: 95%; 
                        height: 90%; 
                        padding: 20px; 
                        display: flex; 
                        flex-direction: column; 
                    }
                    .script-slot-btn.active { background: #4caf50 !important; }
                    .save-load-panel { display: flex; flex-direction: column; gap: 8px; }
                </style>
                </head>
                <body>

                <div class='side-panel'>
                    " + settingsButton + @"
                    <div class='save-load-panel'>
                        " + GenerateSaveLoadButtonsHtml() + @"
                    </div>
                </div>

                <div style='margin-left:200px; padding:12px;'>
                    <h1>OSC Remote Control Panel</h1>
                    <div class=""controls"">
                        <div id=""sort-buttons"">
                            <button id=""sort-default"" class=""active"">Unsorted</button>
                        </div>
                    </div>

                    <div id=""parameters-grid""></div>
                    
                    " + GetBlocklyModalHtml() + @"
                    
                    " + injectAvatarToggle + @"

                    <footer>
                        <p>Current OSC avatar ID: " + (currentID != "none" ? currentID : "no avatar loaded") + @"</p>
                    </footer>
                </div>

                <script>
                    const ws = new WebSocket(`" + DNS + @"/ws`);
                    ws.onopen = () => console.log('WebSocket connected');
                    ws.onmessage = event => {
                        try {
                            const data = JSON.parse(event.data);
                            if (data.action === 'updateHtml') {
                                window.location.reload();
                            }
                        } catch (err) {
                            console.warn('ws error', err);
                        }
                    };
                    
                    function saveState(slot) {
                        fetch(`/saveState?slot=${slot}`, { method:'POST' });
                    }
                    
                    function loadState(slot) {
                        fetch(`/loadState?slot=${slot}`).then(r => r.json());
                    }
                </script>
                </body>
            </html>";
        }
        */
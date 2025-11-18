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
using Jint;

namespace OscWebServer
{
    class Program
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
        private static int currentAvatarState = -1;

        // HTML placeholders
        private static string avatarsettingsHTML = "..."; // your HTML string here

        public static Dictionary<(string, string), bool> lockStates = new Dictionary<(string, string), bool>();
        public static Dictionary<(string, string), float> parametersStates = new Dictionary<(string, string), float>();
        private static Dictionary<string, string> vRCDefaultParameters = new Dictionary<string, string>
        {
            { "IsLocal", "Bool" },
            { "PreviewMode", "Int" },
            { "Viseme", "Int" },
            { "Voice", "Float" },
            { "GestureLeft", "Int" },
            { "GestureRight", "Int" },
            { "GestureLeftWeight", "Float" },
            { "GestureRightWeight", "Float" },
            { "AngularY", "Float" },
            { "VelocityX", "Float" },
            { "VelocityY", "Float" },
            { "VelocityZ", "Float" },
            { "VelocityMagnitude", "Float" },
            { "Upright", "Float" },
            { "Grounded", "Bool" },
            { "Seated", "Bool" },
            { "AFK", "Bool" },
            { "TrackingType", "Int" },
            { "VRMode", "Int" },
            { "MuteSelf", "Bool" },
            { "InStation", "Bool" },
            { "Earmuffs", "Bool" },
            { "IsOnFriendsList", "Bool" },
            { "AvatarVersion", "Int" },
            { "IsAnimatorEnabled", "Bool" },

            // Avatar Scaling Parameters
            { "ScaleModified", "Bool" },
            { "ScaleFactor", "Float" },
            { "ScaleFactorInverse", "Float" },
            { "EyeHeightAsMeters", "Float" },
            { "EyeHeightAsPercent", "Float" },
        };


        public static Dictionary<string, JObject> ScriptStore = new Dictionary<string, JObject>();

        // Script engine state
        private static Dictionary<int, ScriptContext> activeScripts = new Dictionary<int, ScriptContext>();
        private static System.Timers.Timer updateTimer;
        private static System.Timers.Timer networkUpdateTimer;
        private static string PreviousLog;
        #endregion

        #region Script Engine
        public class ScriptContext
        {
            public int Slot { get; set; }
            public string AvatarID { get; set; }
            public Engine JintEngine { get; set; }
            public string GeneratedCode { get; set; }
            public Dictionary<string, object> UserVariables { get; set; } = new();
            public bool IsRunning { get; set; }
        }

        private static void InitializeScriptEngine()
        {
            // 60 FPS update timer
            updateTimer = new System.Timers.Timer(1000.0 / 60.0);
            updateTimer.Elapsed += (s, e) => TriggerUpdateEvent();
            updateTimer.Start();

            // 10 FPS network update timer
            networkUpdateTimer = new System.Timers.Timer(1000.0 / 10.0);
            networkUpdateTimer.Elapsed += (s, e) => TriggerNetworkUpdateEvent();
            networkUpdateTimer.Start();

            Console.WriteLine("Script engine initialized with Update (60Hz) and NetworkUpdate (10Hz) timers.");
        }

        private static void LoadAndStartScript(int slot)
        {
            if (CurrentID == "none") return;

            var filePath = Path.Combine("scripts", $"{CurrentID}_script_state_{currentAvatarState}_{slot}.json");
            if (!File.Exists(filePath)) return;

            try
            {
                var json = File.ReadAllText(filePath);
                var state = JsonConvert.DeserializeObject<ScriptState>(json);

                if (string.IsNullOrEmpty(state?.GeneratedScript))
                {
                    Console.WriteLine($"No script found in slot {slot}");
                    return;
                }

                StopScript(slot);

                var context = new ScriptContext
                {
                    Slot = slot,
                    AvatarID = CurrentID,
                    GeneratedCode = state.GeneratedScript,
                    UserVariables = state.UserVariables ?? new Dictionary<string, object>(),
                    IsRunning = true
                };

                var engine = new Engine(options =>
                {
                    //options.TimeoutInterval(TimeSpan.FromDays(Double.MaxValue));
                    //options.LimitRecursion(0);
                });

                // Bridge OSC parameters to JavaScript
                BridgeParametersToScript(engine);

                // Add custom API functions
                engine.SetValue("setParameter", new Action<string, object, string>((name, value, type) =>
                {
                    SetParameterFromScript(name, value, type);
                }));

                engine.SetValue("getParameter", new Func<string, string, object>((name, type) =>
                {
                    return GetParameterFromScript(name, type);
                }));

                engine.SetValue("log", new Action<object>((msg) =>
                {
                    Console.WriteLine($"[Script {slot}] {msg}");
                    var broadcastMessage = "{\"action\":\"logtoscriptconsole\",\"message\":\"[Script " + slot + "] " + msg + "\"}";
                    if (PreviousLog != broadcastMessage)
                    {
                        PreviousLog = broadcastMessage;
                        WebSocketHandler.BroadcastMessage(broadcastMessage);
                    }
                }));

                engine.SetValue("sendOSC", new Action<string, object, string>((param, value, type) =>
                {
                    SendOscFromScript(param, value, type);
                }));

                context.JintEngine = engine;
                activeScripts[slot] = context;

                // Execute the script to define functions
                engine.Execute(state.GeneratedScript);

                // Trigger OnStart event
                TriggerScriptEvent(slot, "onStart");

                Console.WriteLine($"Script loaded and started for slot {slot}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading script for slot {slot}: {ex.Message}");
            }
        }

        private static void StopScript(int slot)
        {
            if (activeScripts.ContainsKey(slot))
            {
                activeScripts[slot].IsRunning = false;
                activeScripts.Remove(slot);
                Console.WriteLine($"Script stopped for slot {slot}");
            }
        }

        public static void StopAllScript()
        {
            foreach (var slot in activeScripts.Keys.ToList())
            {
                if (activeScripts.ContainsKey(slot))
                {
                    activeScripts[slot].IsRunning = false;
                    activeScripts.Remove(slot);
                    Console.WriteLine($"Script stopped for slot {slot}");
                }
            }
        }

        private static void BridgeParametersToScript(Engine engine)
        {
            foreach (var param in parametersStates)
            {
                var cleanName = param.Key.Item1.Replace("/avatar/parameters/", "").Replace("/", "_");
                engine.SetValue(cleanName, param.Value);
            }
        }

        private static object GetParameterFromScript(string name, string type)
        {
            var address = $"/avatar/parameters/{name}";
            if (parametersStates.TryGetValue((address, type), out var value))
            {
                if (type == "Bool")
                {
                    return value != 0f;
                }
                return value;
            }
            return 0f;
        }

        private static void SetParameterFromScript(string name, object value, string type)
        {
            var address = $"/avatar/parameters/{name}";
            float floatValue = Convert.ToSingle(value);
            if (parametersStates.ContainsKey((address, type)))
            {
                parametersStates[(address, type)] = floatValue;
                SendOscFromScript(address, floatValue, type);
            }
            else
            {
                parametersStates.Add((address, type), floatValue);
                SendOscFromScript(address, floatValue, type);
            }
        }

        private static async void SendOscFromScript(string param, object value, string type)
        {
            try
            {
                var oscSender = new UdpClient("127.0.0.1", oscPortOut);

                if (type == "Float" && float.TryParse(value.ToString(), out var floatValue))
                {
                    var message = new CoreOSC.OscMessage(new CoreOSC.Address(param), new object[] { floatValue });
                    await oscSender.SendMessageAsync(message);
                }
                else if (type == "Int" && int.TryParse(value.ToString(), out var intValue))
                {
                    var message = new CoreOSC.OscMessage(new CoreOSC.Address(param), new object[] { intValue });
                    await oscSender.SendMessageAsync(message);
                }
                else if (type == "Bool")
                {
                    var boolValue = value.ToString().ToLower() == "true" || value.ToString() == "1";
                    var message = new OscMessage(
                        address: new Address(param),
                        arguments: new object[] { boolValue ? CoreOSC.OscTrue.True : CoreOSC.OscFalse.False });
                    await oscSender.SendMessageAsync(message);
                }
                else if (type == "String")
                {
                    var message = new OscMessage(
                        address: new Address(param),
                        arguments: new object[] { value });
                    await oscSender.SendMessageAsync(message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending OSC from script: {ex.Message}");
                var broadcastMessage = "{\"action\":\"logtoscriptconsole\",\"message\":\"Error sending OSC from script : " + ex.Message + "\"}";
                if (PreviousLog != broadcastMessage)
                {
                    PreviousLog = broadcastMessage;
                    WebSocketHandler.BroadcastMessage(broadcastMessage);
                }
            }
        }

        private static void TriggerScriptEvent(int slot, string eventName, params object[] args)
        {
            if (!activeScripts.ContainsKey(slot) || !activeScripts[slot].IsRunning)
                return;

            try
            {
                var engine = activeScripts[slot].JintEngine;
                var function = engine.GetValue(eventName);

                if (function.IsUndefined())
                    return;

                engine.Invoke(eventName, args);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Can only invoke functions") || ex.Message.Contains("Index was outside the bounds of the array.") || ex.Message.Contains("The given key 'delegate' was not present in the dictionary.")) return;
                Console.WriteLine($"Error executing {eventName} in slot {slot}: {ex.Message}");
                var broadcastMessage = "{\"action\":\"logtoscriptconsole\",\"message\":\"Error in script slot " + slot + " during " + eventName + ": " + ex.Message + "\"}";
                if (PreviousLog != broadcastMessage)
                {
                    PreviousLog = "Error in script slot " + slot + " during " + ": " + ex.Message;
                    WebSocketHandler.BroadcastMessage(broadcastMessage);
                }
            }
        }

        private static void TriggerUpdateEvent()
        {
            foreach (var slot in activeScripts.Keys.ToList())
            {
                TriggerScriptEvent(slot, "onUpdate");
            }
        }

        private static void TriggerNetworkUpdateEvent()
        {
            foreach (var slot in activeScripts.Keys.ToList())
            {
                TriggerScriptEvent(slot, "onNetworkUpdate");
            }
        }

        public static void TriggerParameterChangedEvent(string paramName, object value, string type)
        {
            foreach (var slot in activeScripts.Keys.ToList())
            {
                TriggerScriptEvent(slot, "onParameterChanged", paramName, value, type);
            }
        }
        #endregion


        #region HTML Templates
        private static string GetBlocklyModalHtml()
        {
            string wsProtocol = UseHTTPS ? "wss://" : "ws://";
            string wsUrl = string.IsNullOrEmpty(DNS) ? $"{wsProtocol}{PUBIP}" : $"{wsProtocol}{DNS}";
            if ((webPort != 80 && !UseHTTPS) || (webPort != 443 && UseHTTPS))
                wsUrl += $":{webPort}";

            return $@"
                <div id=""scripting-modal"" style=""display:none;"">
                    <div id=""scripting-content"">
                        <div style=""display:flex; justify-content:space-between; align-items:center; margin-bottom:10px;"">
                            <h2 style=""margin:0;"">Blockly Script Editor</h2>
                            <div style=""display:flex; gap:8px;"">
                                <button onclick=""stopScript()"" style=""background:#f44336; padding:8px 16px; border:none; border-radius:6px; color:#000; cursor:pointer;"">⏹ Stop Script</button>
                                <button onclick=""runScript()"" style=""background:#4caf50; padding:8px 16px; border:none; border-radius:6px; color:#000; cursor:pointer;"">▶ Run Script</button>
                                <button onclick=""saveScript()"" style=""background:#2196f3; padding:8px 16px; border:none; border-radius:6px; color:#fff; cursor:pointer;"">💾 Save</button>
                                <button id=""close-scripting"" onclick=""closeScripting()"" style=""background:#f44336; padding:8px 16px; border:none; border-radius:6px; color:#fff; cursor:pointer;"">✕ Close</button>
                            </div>
                        </div>
                        <div style=""display:flex; gap:10px; height:calc(100% - 60px);"">
                            <div style=""width:200px; background:#0d0d0d; border-radius:6px; padding:10px; overflow-y:auto;"">
                                <h3 style=""margin:0 0 10px 0; font-size:14px;"">Script Slots</h3>
                                {GenerateScriptSlotButtons()}
                            </div>
                            <div id=""blocklyDiv"" style=""flex:1; border-radius:6px;""></div>
                            <div style=""width:400px; display:flex; flex-direction:column; gap:10px;"">
                                <div style=""background:#0d0d0d; border-radius:6px; padding:10px; height:50%;"">
                                    <h3 style=""margin:0 0 10px 0; font-size:14px;"">Generated Code</h3>
                                    <pre id=""generatedCode"" style=""background:#000; padding:10px; border-radius:4px; font-family:monospace; font-size:12px; overflow:auto; height:calc(100% - 30px); margin:0;""></pre>
                                </div>
                                <div style=""background:#0d0d0d; border-radius:6px; padding:10px; height:50%;"">
                                    <h3 style=""margin:0 0 10px 0; font-size:14px;"">Console Output</h3>
                                    <pre id=""scriptConsole"" style=""background:#000; padding:10px; border-radius:4px; font-family:monospace; font-size:12px; overflow:auto; height:calc(100% - 30px); margin:0;""></pre>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>

                <script>
                    let blocklyWorkspace;
                    let currentScriptSlot = 0;

                    function openScripting() {{
                        document.getElementById('scripting-modal').style.display = 'flex';
                        if (!blocklyWorkspace) {{
                            initBlockly();
                        }}
                    }}

                    function closeScripting() {{
                        document.getElementById('scripting-modal').style.display = 'none';
                    }}

                    function selectScriptSlot(slot) {{
                        currentScriptSlot = slot;
                        document.querySelectorAll('.script-slot-btn').forEach(b => b.classList.remove('active'));
                        document.getElementById('slot-btn-' + slot).classList.add('active');
                        loadScriptFromSlot(slot);
                    }}

                    function loadScriptFromSlot(slot) {{
                        fetch('/getScript?slot=' + slot)
                            .then(r => r.json())
                            .then(data => {{
                                if (data.workspace) {{
                                    blocklyWorkspace.clear();
                                    const xml = Blockly.utils.xml.textToDom(data.workspace);
                                    Blockly.Xml.domToWorkspace(xml, blocklyWorkspace);
                                }}
                                else {{
                                    blocklyWorkspace.clear();
                                }}
                            }})
                            .catch(err => console.error('Failed to load script:', err));
                    }}

                    function saveScript() {{
                        const xml = Blockly.Xml.workspaceToDom(blocklyWorkspace);
                        const xmlText = Blockly.Xml.domToText(xml);
                        const code = Blockly.JavaScript.workspaceToCode(blocklyWorkspace);

                        fetch('/saveScript', {{
                            method: 'POST',
                            headers: {{ 'Content-Type': 'application/json' }},
                            body: JSON.stringify({{
                                slot: currentScriptSlot,
                                workspace: xmlText,
                                code: code
                            }})
                        }})
                        .then(r => r.text())
                        .then(msg => {{
                            logToConsole('Script saved to slot ' + currentScriptSlot);
                        }});
                    }}

                    function runScript() {{
                        fetch('/runScript?slot=' + currentScriptSlot, {{ method: 'POST' }})
                            .then(r => r.text())
                            .then(msg => {{
                                logToConsole('Script started: ' + msg);
                            }});
                    }}
                    function stopScript() {{
                        fetch('/stopScript?slot=' + currentScriptSlot, {{ method: 'POST' }})
                            .then(r => r.text())
                            .then(msg => {{
                                logToConsole('Script started: ' + msg);
                            }});
                    }}

                    function logToConsole(msg) {{
                        const console = document.getElementById('scriptConsole');
                        const time = new Date().toLocaleTimeString();
                        console.textContent += `[${{time}}] ${{msg}}\n`;
                        console.scrollTop = console.scrollHeight;
                    }}

                    function initBlockly() {{

                        defineCustomBlocks();

                        const toolbox = {{
                            'kind': 'categoryToolbox',
                            'contents': [
                                {{
                                    'kind': 'category',
                                    'name': '🎮 Events',
                                    'colour': '#FF6B6B',
                                    'contents': [
                                        {{'kind': 'block', 'type': 'on_start'}},
                                        {{'kind': 'block', 'type': 'on_parameter_changed'}},
                                        {{'kind': 'block', 'type': 'on_osc_recieved'}},
                                        {{'kind': 'block', 'type': 'on_update'}},
                                        {{'kind': 'block', 'type': 'on_network_update'}}
                                    ]
                                }},
                                {{
                                    'kind': 'category',
                                    'name': '📡 OSC Parameters',
                                    'colour': '#4ECDC4',
                                    'contents': [
                                        {{'kind': 'block', 'type': 'get_parameter'}},
                                        {{'kind': 'block', 'type': 'set_parameter'}},
                                        {{'kind': 'block', 'type': 'send_osc'}},
                                        {{'kind': 'block', 'type': 'send_osc_array'}}
                                    ]
                                }},
                                {{
                                    'kind': 'category',
                                    'name': '🔢 Variables',
                                    'colour': '#A65CA6',
                                    'custom': 'VARIABLE'
                                }},
                                {{
                                    'kind': 'category',
                                    'name': '🔀 Logic',
                                    'colour': '#5C81A6',
                                    'contents': [
                                        {{'kind': 'block', 'type': 'controls_if'}},
                                        {{'kind': 'block', 'type': 'logic_compare'}},
                                        {{'kind': 'block', 'type': 'logic_operation'}},
                                        {{'kind': 'block', 'type': 'logic_boolean'}}
                                    ]
                                }},
                                {{
                                    'kind': 'category',
                                    'name': '🔁 Loops',
                                    'colour': '#5CA65C',
                                    'contents': [
                                        {{'kind': 'block', 'type': 'controls_repeat_ext'}},
                                        {{'kind': 'block', 'type': 'controls_whileUntil'}},
                                        {{'kind': 'block', 'type': 'controls_for'}}
                                    ]
                                }},
                                {{
                                    'kind': 'category',
                                    'name': '➗ Math',
                                    'colour': '#5C68A6',
                                    'contents': [
                                        {{'kind': 'block', 'type': 'math_number'}},
                                        {{'kind': 'block', 'type': 'math_arithmetic'}},
                                        {{'kind': 'block', 'type': 'math_single'}},
                                        {{'kind': 'block', 'type': 'math_round'}}
                                    ]
                                }},
                                {{
                                    'kind': 'category',
                                    'name': '📝 Text',
                                    'colour': '#5CA68D',
                                    'contents': [
                                        {{'kind': 'block', 'type': 'text'}},
                                        {{'kind': 'block', 'type': 'text_print'}},
                                        {{'kind': 'block', 'type': 'text_join'}}
                                    ]
                                }}
                            ]
                        }};

                        blocklyWorkspace = Blockly.inject('blocklyDiv', {{
                            toolbox: toolbox,
                            grid: {{ spacing: 20, length: 3, colour: '#2a2a2a', snap: true }},
                            zoom: {{ controls: true, wheel: true, startScale: 1.0 }},
                            trashcan: true,
                            theme: Blockly.Theme.defineTheme('dark', {{
                                'base': Blockly.Themes.Classic,
                                'componentStyles': {{
                                    'workspaceBackgroundColour': '#1a1a1a',
                                    'toolboxBackgroundColour': '#0d0d0d',
                                    'flyoutBackgroundColour': '#121212'
                                }}
                            }})
                        }});

                        blocklyWorkspace.addChangeListener(() => {{
                            let code = '// No blocks yet';
                            try {{
                                code = Blockly.JavaScript.workspaceToCode(blocklyWorkspace);
                            }} catch (e) {{
                                console.warn('Blockly code generation skipped:', e);
                            }}
                            document.getElementById('generatedCode').textContent = code;
                        }});


                        selectScriptSlot(0);
                    }}

                    function defineCustomBlocks() {{
                        // Event: OnStart
                        Blockly.Blocks['on_start'] = {{
                            init: function() {{
                                this.appendDummyInput().appendField('🎬 When script starts');
                                this.appendStatementInput('DO').setCheck(null);
                                this.setColour('#FF6B6B');
                                this.setTooltip('Runs once when the script is loaded');
                            }}
                        }};
                        Blockly.JavaScript['on_start'] = function(block) {{
                            const statements = Blockly.JavaScript.statementToCode(block, 'DO');
                            return `function onStart() {{\n${{statements}}}}\n`;
                        }};
                        Blockly.JavaScript.forBlock['on_start'] = function(block) {{
                            const statements = Blockly.JavaScript.statementToCode(block, 'DO');
                            return `function onStart() {{\n${{statements}}}}\n`;
                        }};

                        // Event: OnParameterChanged
                        Blockly.Blocks['on_parameter_changed'] = {{
                            init: function () {{
                        
                                const dropdown = new Blockly.FieldDropdown([
                                    ['Select', 'Select'],
                                    ['Custom…', '__custom__'],
                                    // Insert your parameter names
                                    {string.Join(",", parametersStates.Keys.Select(p =>
            {
                var name = p.Item1.Replace("/avatar/parameters/", "").Replace("'", "\\'");
                return $"['{name}','{name}']";
            }))}
                                ], function (newValue) {{
                        
                                    if (newValue === '__custom__') {{
                                        const block = this.getSourceBlock();
                        
                                        // Remove existing container
                                        block.removeInput('PARAM_CONTAINER');
                        
                                        // Add a new PARAM text field
                                        block.appendDummyInput('PARAM_CONTAINER')
                                            .appendField('🔔 When parameter')
                                            .appendField(new Blockly.FieldTextInput('paramName'), 'PARAM')
                                            .appendField(new Blockly.FieldDropdown([
                                                ['Float', 'Float'],
                                                ['Int', 'Int'],
                                                ['Bool', 'Bool'],
                                                ['String', 'String']
                                            ]), 'TYPE')
                                            .appendField('changes');
                                    }}
                                }});
                        
                                // Initial UI (dropdown version)
                                this.appendDummyInput('PARAM_CONTAINER')
                                    .appendField('🔔 When parameter')
                                    .appendField(dropdown, 'PARAM')
                                    .appendField(new Blockly.FieldDropdown([
                                        ['Float', 'Float'],
                                        ['Int', 'Int'],
                                        ['Bool', 'Bool'],
                                        ['String', 'String']
                                    ]), 'TYPE')
                                    .appendField('changes');
                        
                                this.appendStatementInput('DO')
                                    .setCheck(null);
                        
                                this.setColour('#FF6B6B');
                            }}
                        }};
                        Blockly.JavaScript['on_parameter_changed'] = function(block) {{
                            const param = block.getFieldValue('PARAM');
                            const typecheck = block.getFieldValue('TYPE');
                            const statements = Blockly.JavaScript.statementToCode(block, 'DO');
                            return `function onParameterChanged(name, value ,type) {{\n  if (name === '${{param}}' && type === '${{typecheck}}') {{\n${{statements}}  }}\n}}\n`;
                        }};
                        Blockly.JavaScript.forBlock['on_parameter_changed'] = function(block) {{
                            const param = block.getFieldValue('PARAM');
                            const typecheck = block.getFieldValue('TYPE');
                            const statements = Blockly.JavaScript.statementToCode(block, 'DO');
                            return `function onParameterChanged(name, value ,type) {{\n  if (name === '${{param}}' && type === '${{typecheck}}') {{\n${{statements}}  }}\n}}\n`;
                        }};

                        // Event: OnOSCRecieved
                        Blockly.Blocks['on_osc_recieved'] = {{
                            init: function() {{
                                this.appendDummyInput()
                                    .appendField('🔔 When ')
                                    .appendField(new Blockly.FieldTextInput('/address'), 'PARAM')
                                    .appendField(new Blockly.FieldDropdown([['Float', 'Float'], ['Int', 'Int'], ['Bool', 'Bool'], ['String', 'String']]), 'TYPE')
                                    .appendField('recieved');
                                this.appendStatementInput('DO').setCheck(null);
                                this.setColour('#FF6B6B');
                            }}
                        }};
                        Blockly.JavaScript['on_osc_recieved'] = function(block) {{
                            const param = block.getFieldValue('PARAM');
                            const typecheck = block.getFieldValue('TYPE');
                            const statements = Blockly.JavaScript.statementToCode(block, 'DO');
                            return `function onOSCRecieved(name, value, type) {{\n  if (name === '${{param}}' && type === '${{typecheck}}') {{\n${{statements}}  }}\n}}\n`;
                        }};
                        Blockly.JavaScript.forBlock['on_osc_recieved'] = function(block) {{
                            const param = block.getFieldValue('PARAM');
                            const typecheck = block.getFieldValue('TYPE');
                            const statements = Blockly.JavaScript.statementToCode(block, 'DO');
                            return `function onOSCRecieved(name, value, type) {{\n  if (name === '${{param}}' && type === '${{typecheck}}') {{\n${{statements}}  }}\n}}\n`;
                        }};

                        // Event: OnUpdate (60Hz)
                        Blockly.Blocks['on_update'] = {{
                            init: function() {{
                                this.appendDummyInput().appendField('⚡ On Update (60 FPS)');
                                this.appendStatementInput('DO').setCheck(null);
                                this.setColour('#FF6B6B');
                            }}
                        }};
                        Blockly.JavaScript['on_update'] = function(block) {{
                            const statements = Blockly.JavaScript.statementToCode(block, 'DO');
                            return `function onUpdate() {{\n${{statements}}}}\n`;
                        }};

                        Blockly.JavaScript.forBlock['on_update'] = function(block) {{
                            const statements = Blockly.JavaScript.statementToCode(block, 'DO');
                            return `function onUpdate() {{\n${{statements}}}}\n`;
                        }};

                        // Event: OnNetworkUpdate (10Hz)
                        Blockly.Blocks['on_network_update'] = {{
                            init: function() {{
                                this.appendDummyInput().appendField('🌐 On Network Update (10 FPS)');
                                this.appendStatementInput('DO').setCheck(null);
                                this.setColour('#FF6B6B');
                            }}
                        }};
                        Blockly.JavaScript['on_network_update'] = function(block) {{
                            const statements = Blockly.JavaScript.statementToCode(block, 'DO');
                            return `function onNetworkUpdate() {{\n${{statements}}}}\n`;
                        }};
                        Blockly.JavaScript.forBlock['on_network_update'] = function(block) {{
                            const statements = Blockly.JavaScript.statementToCode(block, 'DO');
                            return `function onNetworkUpdate() {{\n${{statements}}}}\n`;
                        }};

                        // Get Parameter
                        Blockly.Blocks['get_parameter'] = {{
                          init: function () {{
                            const dropdown = new Blockly.FieldDropdown([
                              ['Select', 'Select'],
                              ['Custom…', '__custom__'],
                              {string.Join(",", parametersStates.Keys.Select(p =>
            {
                var name = p.Item1.Replace("/avatar/parameters/", "").Replace("'", "\\'");
                return $"['{name}','{name}']";
            }))}
                              ], function(newValue) {{
                              if (newValue === '__custom__') {{
                                const block = this.getSourceBlock();

                                // Remove existing input
                                block.removeInput('PARAM_CONTAINER');

                                // Replace with text input
                                block.appendDummyInput('PARAM_CONTAINER')
                                    .appendField('Get')
                                    .appendField(new Blockly.FieldTextInput('paramName'), 'PARAM')
                                    .appendField(new Blockly.FieldDropdown([['Float', 'Float'], ['Int', 'Int'], ['Bool', 'Bool'], ['String', 'String']]), 'TYPE');

                              }}
                            }});

                            // Initial input
                            this.appendDummyInput('PARAM_CONTAINER')
                                .appendField('Get')
                                .appendField(dropdown, 'PARAM')
                                .appendField(new Blockly.FieldDropdown([['Float', 'Float'], ['Int', 'Int'], ['Bool', 'Bool'], ['String', 'String']]), 'TYPE');


                            this.setOutput(true, 'Number');
                            this.setColour('#4ECDC4');
                          }}
                        }};

                        Blockly.JavaScript['get_parameter'] = function(block) {{
                          const param = block.getFieldValue('PARAM');
                          const typecheck = block.getFieldValue('TYPE');
                          return [`getParameter('${{param}}','${{typecheck}}')`, Blockly.JavaScript.ORDER_FUNCTION_CALL];
                        }};
                        Blockly.JavaScript.forBlock['get_parameter'] = function(block) {{
                          const param = block.getFieldValue('PARAM');
                          const typecheck = block.getFieldValue('TYPE');
                          return [`getParameter('${{param}}','${{typecheck}}')`, Blockly.JavaScript.ORDER_FUNCTION_CALL];
                        }};

                        Blockly.Blocks['set_parameter'] = {{
                          init: function () {{
                            const dropdown = new Blockly.FieldDropdown([
                              ['Select', 'Select'],
                              ['Custom…', '__custom__'],
                              {string.Join(",", parametersStates.Keys.Select(p =>
            {
                var name = p.Item1.Replace("/avatar/parameters/", "").Replace("'", "\\'");
                return $"['{name}','{name}']";
            }))}
                            ], function(newValue) {{
                              if (newValue === '__custom__') {{
                                // Replace dropdown with text input
                                this.getSourceBlock().removeInput('PARAM_CONTAINER');
                                this.getSourceBlock().appendDummyInput('PARAM_CONTAINER')
                                    .appendField('Set')
                                    .appendField(new Blockly.FieldTextInput('paramName'), 'PARAM')
                                    .appendField(new Blockly.FieldDropdown([['Float', 'Float'], ['Int', 'Int'], ['Bool', 'Bool'], ['String', 'String']]), 'TYPE')
                                    .appendField('to');
                              }}
                            }});
                        
                            this.appendDummyInput('PARAM_CONTAINER')
                                .appendField('Set')
                                .appendField(dropdown, 'PARAM')
                                .appendField(new Blockly.FieldDropdown([['Float', 'Float'], ['Int', 'Int'], ['Bool', 'Bool'], ['String', 'String']]), 'TYPE')
                                .appendField('to');
                        
                            this.appendValueInput('VALUE');
                        
                            this.setPreviousStatement(true);
                            this.setNextStatement(true);
                            this.setColour('#4ECDC4');
                          }}
                        }};
                        Blockly.JavaScript['set_parameter'] = function(block) {{
                            const param = block.getFieldValue('PARAM');
                            const typecheck = block.getFieldValue('TYPE');
                            const value = Blockly.JavaScript.valueToCode(block, 'VALUE', Blockly.JavaScript.ORDER_NONE) || '0';
                            return `setParameter('${{param}}', ${{value}}, '${{typecheck}}');\n`;
                        }};

                        Blockly.JavaScript.forBlock['set_parameter'] = function(block) {{
                            const param = block.getFieldValue('PARAM');
                            const typecheck = block.getFieldValue('TYPE');
                            const value = Blockly.JavaScript.valueToCode(block, 'VALUE', Blockly.JavaScript.ORDER_NONE) || '0';
                            return `setParameter('${{param}}', ${{value}}, '${{typecheck}}');\n`;
                        }};



                        // Send OSC
                        Blockly.Blocks['send_osc'] = {{
                            init: function() {{
                                this.appendValueInput('VALUE')
                                    .appendField('Send OSC')
                                    .appendField(new Blockly.FieldTextInput('/address'), 'PARAM')
                                    .appendField(new Blockly.FieldDropdown([['Float', 'Float'], ['Int', 'Int'], ['Bool', 'Bool'], ['String', 'String']]), 'TYPE')
                                    .appendField('value');
                                this.setPreviousStatement(true, null);
                                this.setNextStatement(true, null);
                                this.setColour('#4ECDC4');
                            }}
                        }};
                        Blockly.JavaScript['send_osc'] = function(block) {{
                            const param = block.getFieldValue('PARAM');
                            const type = block.getFieldValue('TYPE');
                            const value = Blockly.JavaScript.valueToCode(block, 'VALUE', Blockly.JavaScript.ORDER_NONE) || '0';
                            return `sendOSC('${{param}}', ${{value}}, '${{type}}');\n`;
                        }};
                        Blockly.JavaScript.forBlock['send_osc'] = function(block) {{
                            const param = block.getFieldValue('PARAM');
                            const type = block.getFieldValue('TYPE');
                            const value = Blockly.JavaScript.valueToCode(block, 'VALUE', Blockly.JavaScript.ORDER_NONE) || '0';
                            return `sendOSC('${{param}}', ${{value}}, '${{type}}');\n`;
                        }};

                        Blockly.Blocks['send_osc_array'] = {{
                            init: function() {{
                                this.itemCount_ = 1;

                                this.setPreviousStatement(true, null);
                                this.setNextStatement(true, null);
                                this.setColour('#4ECDC4');

                                this.appendDummyInput()
                                    .appendField('Send OSC')
                                    .appendField(new Blockly.FieldTextInput('/address'), 'PARAM')
                                    .appendField('with')
                                    .appendField(new Blockly.FieldNumber(1, 1, 999, 1, this.updateShape_.bind(this)),'ARRAY_SIZE')
                                    .appendField('values');

                                this.updateShape_();
                            }},

                            updateShape_: function() {{
                                // Remove all existing value inputs
                                let i = 0;
                                while (this.getInput('ITEM' + i)) {{
                                    this.removeInput('ITEM' + i);
                                    i++;
                                }}

                                // Get the desired array size from the number field
                                var size = this.getFieldValue('ARRAY_SIZE');
                                this.itemCount_ = parseInt(size) || 1;

                                // Add new item inputs based on array size
                                for (let i = 0; i < this.itemCount_; i++) {{
                                    this.appendValueInput('ITEM' + i)
                                        .setCheck(null)
                                        .appendField('value ' + (i + 1));
                                }}
                            }}
                        }};

                        Blockly.JavaScript['send_osc_array'] = function(block) {{
                            var param = block.getFieldValue('PARAM');
                            var items = [];
                            for (let i = 0; i < block.itemCount_; i++) {{
                                var v = Blockly.JavaScript.valueToCode(block, 'ITEM' + i, Blockly.JavaScript.ORDER_NONE) || 'null';
                                items.push(v);
                            }}
                            return `sendOSC('${{param}}', [${{items.join(', ')}}]);\\n`;
                        }};
                        Blockly.JavaScript.forBlock['send_osc_array'] = function(block) {{
                            var param = block.getFieldValue('PARAM');
                            var items = [];
                            for (let i = 0; i < block.itemCount_; i++) {{
                                var v = Blockly.JavaScript.valueToCode(block, 'ITEM' + i, Blockly.JavaScript.ORDER_NONE) || 'null';
                                items.push(v);
                            }}
                            return `sendOSC('${{param}}', [${{items.join(', ')}}]);\\n`;
                        }};
                    }}
                </script>";
        }

        private static string GenerateScriptSlotButtons()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < saveCount; i++)
            {
                var activeClass = i == 0 ? "active" : "";
                sb.AppendLine($@"<button id=""slot-btn-{i}"" class=""script-slot-btn {activeClass}"" onclick=""selectScriptSlot({i})"" style=""width:100%; padding:8px; margin:4px 0; border:none; border-radius:4px; background:#333; color:#e0e0e0; cursor:pointer;"">Slot #{i}</button>");
            }
            return sb.ToString();
        }

        private static string SimpleRedirect()
        {
            return @"<!DOCTYPE html>
                <html lang=""en"">
                <head>
                    <meta charset=""UTF-8"">
                    <meta http-equiv=""refresh"" content=""0; url=" + httpsTXTReplace + DNS + @"/"" />
                    <title>Redirecting...</title>
                </head>
                <body>
                    <p>If you are not redirected automatically, follow this <a href=""" + httpsTXTReplace + DNS + @"/"">link</a>.</p>
                </body>
                </html>";
        }

        private static string GetParametersHtml()
        {
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
                            .controls input[type='search'] { padding:6px 8px; border-radius:6px; border:1px solid #333; background:#111; color:#e0e0e0; }
                            #parameters-grid {display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 14px; align-items: start;}
                            .parameter-container { border:1px solid #333; padding:10px; border-radius:8px; background:#1a1a1a; display:flex; flex-direction:column; gap:8px; }
                            .side-panel { position: fixed; left: 0; top: 0; width: 180px; height: 100%; background: #1a1a1a; border-right: 1px solid #333; padding: 10px; display: flex; flex-direction: column; gap: 8px; overflow-y: auto; }
                            .side-panel button { padding: 6px 8px; border-radius: 6px; border: none; background: #333; color: #e0e0e0; cursor: pointer; font-size: 0.9rem; }
                            .side-panel button.active { background: #4caf50; color: #061206; }
                            .button-container { display: flex; gap: 8px; }
                            .save-load-panel { display: flex; flex-direction: column; gap: 8px; }
                            footer { margin-top:14px; color:#999; }
                        </style>
                        </head>
                        <body>

                        <div class='side-panel'>
                            <button onclick=""location.href='/'"">Main</button>
                            <div></div>
                            <button>Nothing but cricket</button>
                            <div></div>
                            <button onclick=""location.href='/'"">Add new</button>
                        </div>

                        <div style='margin-left:200px; padding:12px;'>
                            <h1>YAVOT Parameters blacklist/whitelist</h1>

                            <div class=""controls"">
                                <div id=""sort-buttons"">
                                    <button id=""sort-default"" class=""active"">Unsorted</button>
                                    <button id=""sort-name"">By Name</button>
                                    <button id=""sort-type"">By Type</button>
                                    <button id=""sort-group"">By Groups</button>
                                    <button id=""sort-minimal"">Minimalist</button>
                                </div>
                                <input id=""param-search"" type=""search"" placeholder=""Search parameters..."">
                            </div>

                            <div id=""parameters-grid""></div>

                            <footer>
                                <p>Current OSC avatar ID: none</p>
                            </footer>
                        </div>

                        </body>
                        </html>";
        }

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
                    /* --- Colors & base --- */
                    body { background-color: #121212; color: #e0e0e0; font-family: 'Segoe UI', Arial, sans-serif; padding: 12px; }
                    h1 { margin: 8px 0 12px 0; }

                    /* Top controls */
                    .top-bar { display:flex; gap:12px; align-items:center; flex-wrap:wrap; margin-bottom:12px; }
                    .save-load-row { display:flex; gap:8px; flex-wrap:wrap; }

                    /* Sort/Search bar */
                    .controls { display:flex; gap:8px; align-items:center; margin: 10px 0; }
                    .controls button { padding:6px 10px; border-radius:6px; border:none; cursor:pointer; background:#333; color:#e0e0e0; }
                    .controls button.active { background:#4caf50; color:#061206; }
                    .controls input[type='search'] { padding:6px 8px; border-radius:6px; border:1px solid #333; background:#111; color:#e0e0e0; }

                    /* Grid */
                    #parameters-grid {display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 14px; align-items: start;}

                    /* Parameter card */
                    .parameter-container { height: auto; border:1px solid #333; padding:10px; border-radius:8px; background:#1a1a1a; display:flex; flex-direction:column; gap:8px; }
                    .param-header { font-weight:700; text-align:center; }
                    .range-wrapper { display:flex; align-items:center; gap:10px; }
                    .range-slider { flex:1; }
                    .value-input {padding:8px; border-radius:4px; border:1px solid #555; background:#222; color:#fff; text-align:center; }

                    /* Bool style */
                    .toggle-container { display:flex; align-items:center; gap:8px; justify-content:space-between; }
                    .bool-button { flex:1; padding:8px; border-radius:8px; border:none; cursor:pointer; font-weight:700; text-align:center; }
                    .bool-button.on { background:#4caf50; }
                    .bool-button.off { background:#555; color:#ccc; }

                    .lock-button { padding:6px 8px; border-radius:6px; border:none; cursor:pointer; background:#888; color:#fff; }
                    .lock-button.locked { background:#f44336; }

                    /* Group folders */
                    .group-folder { height: auto; border:1px solid #2b2b2b; background:#161616; border-radius:8px; padding:8px; }
                    .group-header { cursor:pointer; font-weight:700; padding:6px; border-radius:6px; background:#111; }
                    .group-content { margin-top:8px; display:none; }

                    /* --- Side panel --- */
                    .side-panel { position: fixed; left: 0; top: 0; width: 180px; height: 100%; background: #1a1a1a; border-right: 1px solid #333; padding: 10px; display: flex; flex-direction: column; gap: 8px; overflow-y: auto; }
                    .side-panel button { padding: 6px 8px; border-radius: 6px; border: none; background: #333; color: #e0e0e0; cursor: pointer; font-size: 0.9rem; }
                    .side-panel button.active { background: #4caf50; color: #061206; }
                    .button-container { display: flex; gap: 8px; }
                    .save-load-panel { display: flex; flex-direction: column; gap: 8px; }
                    
                    /* --- Scripting Modal --- */
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

                    /* --- Minimalist View --- */
                    .minimal-overlay {
                        background: rgba(255,255,255,0.04);
                        border: 1px solid rgba(255,255,255,0.08);
                        padding: 7px 10px;
                        border-radius: 6px;
                        margin-bottom: 4px;
                        display: flex;
                        justify-content: space-between;
                        font-size: 14px;
                    }

                    .min-name { font-weight: 600; }
                    .min-value { opacity: 0.8; }

                    .min-value.blink {
                        animation: blinkHighlight 0.4s ease;
                    }
                    .minimal-overlay.blink {
                        animation: blinkHighlight 0.4s ease;
                    }

                    @keyframes blinkHighlight {
                        0%   { background-color: rgba(255, 255, 0, 0.6); }
                        100% { background-color: transparent; }
                    }




                    footer { margin-top:14px; color:#999; }
                </style>
                </head>
                <body>

                <div class='side-panel'>
                    " + settingsButton + @"
                    <div class='save-load-panel'>
                        " + GenerateSaveLoadButtonsHtml() + @"
                    </div>
                    <div>   
                    </div>
                </div>

                <div style='margin-left:200px; padding:12px;'>
                    <h1>OSC Remote Control Panel</h1>
                    <div class=""controls"">
                        <div id=""sort-buttons"">
                            <button id=""sort-default"" onclick=""setSort('default')"" class=""active"">Unsorted (Default)</button>
                            <button id=""sort-name"" onclick=""setSort('name')"" >By Name</button>
                            <button id=""sort-type"" onclick=""setSort('type')"">By Type</button>
                            <button id=""sort-group"" onclick=""setSort('group')"">By Groups</button>
                            <button id=""sort-minimal"" onclick=""setSort('minimal')"">Minimalist</button>
                        </div>
                        <input id=""param-search"" type=""search"" placeholder=""Search parameters (name / group)..."">
                    </div>

                    <!-- Parameter grid (populated by client JS) -->
                    <div id=""parameters-grid""></div>

                    " + GetBlocklyModalHtml() + @"
                    
                    " + injectAvatarToggle + @"

                        <footer>
                            <p>Current OSC avatar ID : " + (currentID != "none" ? currentID : "no avatar loaded") + @"</p>
                        </footer>
                </div>

                <!-- Common JS (rendering, sorting, saving/loading, websocket) -->
                <script>
                    // ParameterList will be set in the server-injected JS (if an avatar is loaded)
                    window.ParameterList = window.ParameterList || [];

                    // Render helpers
                    function renderParameters(list) {
                        const grid = document.getElementById('parameters-grid');
                        grid.innerHTML = '';
                        list.forEach(p => {
                            // Insert DOM: p.html is a full HTML string for the card
                            const wrapper = document.createElement('div');
                            wrapper.innerHTML = p.html;
                            // Ensure IDs are unique in DOM (they were using the param id already)
                            grid.appendChild(wrapper);
                        });
                    }

                    function setActiveSortButton(mode) {
                        document.querySelectorAll('#sort-buttons button').forEach(b => b.classList.remove('active'));
                        if (mode === 'default') document.getElementById('sort-default').classList.add('active');
                        if (mode === 'name') document.getElementById('sort-name').classList.add('active');
                        if (mode === 'type') document.getElementById('sort-type').classList.add('active');
                        if (mode === 'group') document.getElementById('sort-group').classList.add('active');
                        if (mode === 'minimal') document.getElementById('sort-minimal').classList.add('active');
                    }

                    function setSort(mode) {
                        setActiveSortButton(mode);
                        const searchQuery = document.getElementById('param-search').value.trim().toLowerCase();
                        let list = window.ParameterList.slice();

                        // filter by search
                        if (searchQuery) {
                            list = list.filter(p => (p.name && p.name.toLowerCase().includes(searchQuery)) ||
                                                    (p.group && p.group.toLowerCase().includes(searchQuery)));
                        }

                        if (mode === 'name') list.sort((a,b) => a.name.localeCompare(b.name));
                        else if (mode === 'minimal') {
                            const grid = document.getElementById('parameters-grid');

                            // ensure parameters are rendered normally first
                            renderParameters(list);

                            // now convert each item into minimal view overlay
                            Array.from(grid.children).forEach(container => {
                                const paramEl = container.querySelector('.parameter-container');
                                if (!paramEl) return;

                                const name = paramEl.querySelector('.param-header')?.textContent || '';
                                const input = paramEl.querySelector('.value-input');
                                const toggle = paramEl.querySelector('.bool-button');
                                
                                let paramId = '';
                                if (input) paramId = input.id.replace('input-','');
                                else if (toggle) paramId = toggle.id;

                                let value = '';
                                if (input) value = input.value;
                                else if (toggle) value = toggle.classList.contains('on') ? 'True' : 'False';

                                // hide full control
                                paramEl.style.display = 'none';

                                // add minimal overlay
                                const min = document.createElement('div');
                                min.className = 'minimal-overlay';
                                min.innerHTML = `
                                    <div class='min-name'>${name}</div>
                                    <div class='min-value' id= '${paramId}_minimal_value'>${value}</div>
                                `;
                                container.appendChild(min);
                            });

                            return;
                        }
                        else if (mode === 'type') list.sort((a,b) => a.type.localeCompare(b.type));
                        else if (mode === 'group') {
                            // group the items into hierarchical foldouts
                            const groups = {};
                            list.forEach(p => {
                                const g = p.group && p.group.length ? p.group : 'Ungrouped';
                                if (!groups[g]) groups[g] = [];
                                groups[g].push(p);
                            });

                            const grid = document.getElementById('parameters-grid');
                            grid.innerHTML = '';

                            Object.keys(groups).sort().forEach(groupName => {
                                const idSafe = 'grp_' + groupName.replace(/[^a-z0-9]/gi,'_');
                                const folder = document.createElement('div');
                                folder.className = 'group-folder';

                                const header = document.createElement('div');
                                header.className = 'group-header';
                                header.textContent = groupName;
                                header.onclick = () => {
                                    const content = folder.querySelector('.group-content');
                                    content.style.display = (content.style.display === 'none') ? 'block' : 'none';
                                };

                                const content = document.createElement('div');
                                content.className = 'group-content';
                                groups[groupName].forEach(p => {
                                    const tmp = document.createElement('div');
                                    tmp.innerHTML = p.html;
                                    content.appendChild(tmp);
                                });

                                folder.appendChild(header);
                                folder.appendChild(content);
                                grid.appendChild(folder);
                            });
                            return;
                        }

                        // default rendering: flat grid
                        renderParameters(list);
                    }

                    // search live
                    document.getElementById('param-search').addEventListener('input', () => {
                        // reapply current active mode
                        const active = document.querySelector('#sort-buttons button.active');
                        const mode = active ? (active.id.replace('sort-','')) : 'default';
                        setSort(mode);
                    });

                    function extractValueFromHtml(html) {
                        // Create a temporary element so we can query the HTML
                        const tmp = document.createElement('div');
                        tmp.innerHTML = html;

                        // Bool uses a button with class 'on/off'
                        const boolBtn = tmp.querySelector('.bool-button');
                        if (boolBtn) return boolBtn.classList.contains('on') ? 'True' : 'False';

                        // Float/Int use an <input value=''>
                        const input = tmp.querySelector('input.value-input');
                        if (input) return input.value;

                        return '';
                    }

                    // initial render
                    document.addEventListener('DOMContentLoaded', () => setSort('default'));

                    // Preserve your websocket if you use it to update UI
                    const ws = new WebSocket(`" + DNS + @"/ws`);
                    ws.onopen = () => console.log('WebSocket connection established.');
                    ws.onclose = () => console.log('WebSocket connection closed.');
                    ws.onmessage = event => {
                        try {
                            const data = JSON.parse(event.data);

                            if (data.action === 'updateHtml') {
                                window.location.reload();
                            } 
                            else if (data.action === 'logtoscriptconsole') {
                                logToConsole(data.message);
                            } 
                            else if (data.param) {

                                const param = data.param;
                                const value = data.value;

                                // FLOAT/INT input update
                                const input = document.getElementById(`input-${param}`);
                                if (input) {
                                    input.value = value;
                                    const id = param;
                                    const display = document.getElementById(`value-${id}`);
                                    if (display) display.textContent = parseFloat(value).toFixed(3);
                                }

                                // BOOL update
                                const toggleButton = document.getElementById(param);
                                if (toggleButton) {
                                    const isOn = value === 'f';
                                    toggleButton.classList.toggle('on', !isOn);
                                    toggleButton.classList.toggle('off', isOn);
                                }
                                const container = document.getElementById(param)?.closest('div');

                                if (true) {
                                    const minOverlay = document.getElementById(`${param}_minimal_value`);
                                    if (minOverlay) {
                                
                                        // Update displayed value
                                        if (value !== 'f' && value !== 't') {
                                            minOverlay.textContent = value;
                                        } else {
                                            const isOn = value === 'f';
                                            minOverlay.textContent = isOn ? 'False' : 'True';
                                        }
                                
                                        const overlayParent = minOverlay.closest('.minimal-overlay');
                                        if (overlayParent) {
                                            overlayParent.classList.remove('blink');   // reset in case still animating
                                            void overlayParent.offsetWidth;            // force reflow
                                            overlayParent.classList.add('blink');      // trigger animation
                                        }else{
                                            minOverlay.classList.remove('blink');  // reset if still playing
                                            void minOverlay.offsetWidth;           // force reflow so animation restarts
                                            minOverlay.classList.add('blink');
                                        }
                                    }
                                }

                            }

                        } catch (err) {
                            console.warn('ws message parse error', err);
                        }
                    };
                    
                </script>

                <!-- Existing JS helpers (locks, sendOsc, save/load) -->
                <script>
                function toggleLock(param, type , button) {
                    const locked = button.classList.toggle('locked');
                    fetch(`https://catalyss.ddns.net/togglelock?param=${encodeURIComponent(param)}&type=${encodeURIComponent(type)}&locked=${locked}`);
                }

                function updateSliderFromInput(id, value, reff, type) {
                    const slider = document.getElementById(`slider-${id}`);
                    if (!slider) return;
                    slider.value = value;
                    updateValueDisplay(id, value);
                    sendOsc(reff, value, type);
                }

                function sendOsc(param, value, type) {
                    fetch(`/sendOsc?param=${encodeURIComponent(param)}&value=${encodeURIComponent(value)}&type=${encodeURIComponent(type)}`);
                }

                function updateValueDisplay(param, value) {
                    const el = document.getElementById(`value-${param}`);
                    if (el) el.textContent = parseFloat(value).toFixed(3);
                }

                function adjustSlider(id, parm, increment) {
                    const slider = document.getElementById(`slider-${parm}`);
                    if (!slider) return;
                    let newValue = parseFloat(slider.value) + increment;
                    if (newValue >= slider.min && newValue <= slider.max) {
                        sendOsc(id, newValue, 'Float');
                        slider.value = newValue.toFixed(3);
                        updateValueDisplay(parm, newValue);
                    }
                }

                function incrementValue(id, parm, amount) {
                    const slider = document.getElementById(`slider-${parm}`);
                    if (!slider) return;
                    let newValue = parseInt(slider.value) + amount;
                    if (newValue >= slider.min && newValue <= slider.max) {
                        sendOsc(id, newValue, 'Int');
                        slider.value = newValue;
                        updateValueDisplay(parm, newValue);
                    }
                }

                function toggleBool(id, button) {
                    const currentValue = button.classList.contains('on');
                    const newValue = !currentValue;
                    button.classList.toggle('on', newValue);
                    button.classList.toggle('off', !newValue);
                    sendOsc(id, newValue ? 't' : 'f', 'Bool');
                }
                </script>

                <!-- Save/load scripts -->
                <script>
                function saveState(slot) {
                    
                    fetch(`/saveState?slot=${slot}&t=${Date.now()}`, { method:'POST', 
                        headers: { 'Content-Type': 'application/json' },
                        body: {'':''} // Dummy body to make it POST),
                    });
                }

                function loadState(slot) {
                    fetch(`/loadState?slot=${slot}`)
                    .then(r => r.json())
                    .then(state => {
                        if (state.Toggles) state.Toggles.forEach(t => {
                            const el = document.getElementById(t.Id);
                            if (el){
                            el.classList.toggle('on',  t.Checked);
                            el.classList.toggle('off',!t.Checked);
                            }
                        });
                        if (state.Sliders) state.Sliders.forEach(s => {
                            const el = document.getElementById(s.Id.replace(/^slider-/, 'input-'));
                            if (el){
                                el.value = s.Value;
                                if (typeof el.oninput === 'function') {
                                    el.oninput();
                                }
                            }
                        });                        
                    });
                }
                </script>
                <script src=""https://unpkg.com/blockly/blockly.min.js""></script>
                </body>
            </html>";
        }
        #endregion

        #region Main Entry
        static async Task Main(string[] args)
        {
            //await BlocklyHandler.Blockly.BLMain(args);
            //return;
            ParseArguments(args);
            await ConfigureHTTPSIfNeeded();
            await FetchPublicIP();
            PrintServerAddress();

            StartOscListener();
            StartOscSender();
            Router("parameters/Routing.json");
            InitializeScriptEngine();
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

        #region OSC
        private static void StartOscListener()
        {
            var udpClient = new UdpClient(oscPortIn);
            Task.Run(async () =>
            {
                while (true)
                {
                    var packet = await udpClient.ReceiveMessageAsync();
                    await HandleOscPacket(packet);
                }
            });
        }

        private static async Task HandleOscPacket(OscMessage packet)
        {
            var messageReceived = packet;
            if (messageReceived.Address.Value == "/avatar/change")
            {
                string value = messageReceived.Arguments.ToArray()[0].ToString();
                if (CheckAvatarId(File.ReadAllText("parameters/AvatarListing.json"), value))
                {
                    Console.WriteLine("Avatar Changed to ID: " + value);
                    if (CurrentID != value)
                    {
                        StopAllScript();
                        currentAvatarState = -1;
                        CurrentID = value;
                        lockStates.Clear();
                        parametersStates.Clear();
                        var broadcastMessage = "{\"action\":\"updateHtml\"}";
                        await WebSocketHandler.BroadcastMessage(broadcastMessage);
                    }
                }
            }
            else
            {
                var type = messageReceived.Arguments.FirstOrDefault()?.GetType().Name;
                type = type.Replace("Single", "Float").Replace("Int32", "Int").Replace("OscFalse", "Bool").Replace("OscTrue", "Bool");
                var param = messageReceived.Address.Value.Replace("/avatar/parameters/", "");
                var value = messageReceived.Arguments.FirstOrDefault()?.ToString()?.Replace("CoreOSC.OscFalse", "f").Replace("CoreOSC.OscTrue", "t");
                if (lockStates.ContainsKey((messageReceived.Address.Value, type)) && lockStates[(messageReceived.Address.Value, type)])
                {
                    Console.WriteLine("OSC Message to " + messageReceived.Address.Value + " is locked. Ignoring.");
                    var checkValue = 1f;
                    if (value != "f" && value != "t") checkValue = float.TryParse(value, out var f) ? f : 0f;
                    else if (value == "t") checkValue = 1f;
                    else checkValue = 0f;

                    if (parametersStates[(messageReceived.Address.Value, type)] == checkValue)
                    {
                        Console.WriteLine("OSC Message to " + messageReceived.Address.Value + " is locked and is same. Ignoring.");
                        return;
                    }

                    var oscSender = new UdpClient("127.0.0.1", oscPortOut);

                    var paramm = messageReceived.Address.Value;
                    var overrideValue = parametersStates[(messageReceived.Address.Value, type)];

                    if (type == "Float")
                    {

                        var message = new CoreOSC.OscMessage(new CoreOSC.Address($"{paramm}"), [overrideValue]);
                        await oscSender.SendMessageAsync(message);
                    }
                    else if (type == "Int")
                    {
                        var message = new CoreOSC.OscMessage(new CoreOSC.Address($"{paramm}"), [overrideValue]);
                        await oscSender.SendMessageAsync(message);
                    }
                    else if (type == "Bool")
                    {
                        var message = new OscMessage();
                        if (overrideValue == 1f)
                        {
                            message = new OscMessage(
                            address: new Address($"{paramm}"),
                            arguments: new object[] { CoreOSC.OscTrue.True });
                        }
                        else
                        {
                            message = new OscMessage(
                            address: new Address($"{paramm}"),
                            arguments: new object[] { CoreOSC.OscFalse.False });
                        }
                        await oscSender.SendMessageAsync(message);
                    }

                    Console.WriteLine("OSC Message to " + messageReceived.Address.Value + " is locked. Ignoring.");
                    return;
                }

                if (value != "f" && value != "t")
                {
                    parametersStates[(messageReceived.Address.Value, type)] = float.TryParse(value, out var f) ? f : 0f;
                    TriggerParameterChangedEvent(param, value == null ? 0f : value, type);
                }
                else
                {
                    parametersStates[(messageReceived.Address.Value, type)] = (value == "t") ? 1f : 0f;
                    TriggerParameterChangedEvent(param, value == "t" ? true : false, type);
                }
                var broadcastMessage = "{\"param\":\"" + param.Replace("/", "_") + "\",\"value\":\"" + value + "\"}";
                await WebSocketHandler.BroadcastMessage(broadcastMessage);
            }
        }

        private static void StartOscSender()
        {
            using (var udpClient = new UdpClient("127.0.0.1", oscPortIn))
            {
                var message = new OscMessage(
                 address: new Address("/Program Started"),
                 arguments: new object[] { "true" });
                udpClient.SendMessageAsync(message).Wait();
            }
        }

        public static bool CheckAvatarId(string json, string input)
        {
            var avatarList = JsonConvert.DeserializeObject<AvatarList>(json);
            bool isInList = Array.Exists(avatarList.AvatarIds, id => id.Equals(input, StringComparison.OrdinalIgnoreCase));
            return avatarList.IsWhiteList ? isInList : !isInList;
        }

        private static async void SendOscMessage(string param, string value, string type)
        {
            var oscSender = new UdpClient("127.0.0.1", oscPortIn);

            if (type == "Float" && float.TryParse(value, out var floatValue))
            {
                if (lockStates.ContainsKey((param, type)) && lockStates[(param, type)]) return;
                parametersStates[(param, type)] = floatValue;
                var message = new CoreOSC.OscMessage(new CoreOSC.Address(param), new object[] { floatValue });
                await oscSender.SendMessageAsync(message);
            }
            else if (type == "Int" && int.TryParse(value, out var intValue))
            {
                if (lockStates.ContainsKey((param, type)) && lockStates[(param, type)]) return;
                parametersStates[(param, type)] = intValue;
                var message = new CoreOSC.OscMessage(new CoreOSC.Address(param), new object[] { intValue });
                await oscSender.SendMessageAsync(message);
            }
            else if (type == "Bool")
            {
                if (lockStates.ContainsKey((param, type)) && lockStates[(param, type)]) return;
                parametersStates[(param, type)] = value == "t" ? 1f : 0f;
                var message = new OscMessage(
                    address: new Address(param),
                    arguments: new object[] { value == "t" ? CoreOSC.OscTrue.True : CoreOSC.OscFalse.False });
                await oscSender.SendMessageAsync(message);
            }
            else if (type == "String")
            {
                if (lockStates.ContainsKey((param, type)) && lockStates[(param, type)]) return;
                parametersStates[(param, type)] = value == "t" ? 1f : 0f;
                var message = new OscMessage(
                    address: new Address(param),
                    arguments: new object[] { value });
                await oscSender.SendMessageAsync(message);
            }
        }
        #endregion

        #region Routing
        public static void Router(string configFilePath)
        {
            var routes = LoadRoutingConfig(configFilePath);
            if (routes == null) return;

            foreach (var route in routes)
            {
                int inputPort = ResolvePort(route.InputPort);
                if (inputPort <= 0) continue;

                var udpClient = new UdpClient(inputPort);
                Task.Run(async () =>
                {
                    while (true)
                    {
                        var packet = await udpClient.ReceiveMessageAsync();
                        foreach (var output in route.Outputs)
                        {
                            int outputPort = ResolvePort(output.Port);
                            if (outputPort > 0)
                            {
                                using var senderClient = new UdpClient(output.Ip, outputPort);
                                await senderClient.SendMessageAsync(packet);
                            }
                        }
                    }
                });
            }
        }

        public static List<UdpRoute> LoadRoutingConfig(string filePath)
        {
            return File.Exists(filePath)
                ? JsonConvert.DeserializeObject<List<UdpRoute>>(File.ReadAllText(filePath))
                : null;
        }

        public static int ResolvePort(string port) =>
            port switch
            {
                "oscPortIn" => oscPortIn,
                "oscPortOut" => oscPortOut,
                _ => int.TryParse(port, out var p) ? p : -1
            };
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
                    options.ListenAnyIP(8080);
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
                            Console.WriteLine("HEY THIS GOT TRIGGERD");
                            // Console.WriteLine(context.WebSockets.IsWebSocketRequest);
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
                        if (context.Request.Path == "/settingsocket" || context.Request.Path == "/" + DNS + "/settingsocket")
                        {
                            Console.WriteLine("settingsocket accepted");
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
            // Map all endpoints here
            endpoints.MapGet("/", async context =>
            {
                Console.WriteLine(context.Connection.RemoteIpAddress.ToString());
                var ishost = IsLocalhostRequest(context);
                string html = GetIndexHtml(ishost, CurrentID);
                await context.Response.WriteAsync(html);
            });

            // Get script from slot
            endpoints.MapGet("/getScript", async context =>
            {
                var slot = int.Parse(context.Request.Query["slot"].ToString());
                var filePath = Path.Combine("scripts", $"{CurrentID}_script_state_{currentAvatarState}_{slot}.json");

                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var scriptState = JsonConvert.DeserializeObject<ScriptState>(json);
                    var result = new
                    {
                        workspace = scriptState?.BlocklyWorkspace ?? "",
                        code = scriptState?.GeneratedScript ?? ""
                    };
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonConvert.SerializeObject(result));
                }
                else
                {
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{}");
                }
            });

            // Save script to slot
            endpoints.MapPost("/saveScript", async context =>
            {

                if (CurrentID == "none")
                {
                    await context.Response.WriteAsync("No valid avatar loaded. Cannot save script.");
                    return;
                }

                var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
                var data = JsonConvert.DeserializeObject<dynamic>(body);

                int slot = (int)data.slot;
                string workspace = data.workspace;
                string code = data.code;

                var filePath = Path.Combine("scripts", $"{CurrentID}_script_state_{currentAvatarState}_{slot}.json");
                Directory.CreateDirectory("scripts");

                ScriptState scriptState = new ScriptState();
                //if (File.Exists(filePath))
                //{
                //    var json = File.ReadAllText(filePath);
                //    scriptState = JsonConvert.DeserializeObject<ScriptState>(json) ?? new ScriptState();
                //}

                scriptState.BlocklyWorkspace = workspace;
                scriptState.GeneratedScript = code;

                await File.WriteAllTextAsync(filePath, JsonConvert.SerializeObject(scriptState, Newtonsoft.Json.Formatting.Indented));
                await context.Response.WriteAsync($"Script saved to slot {slot}");
            });

            // Run/start script
            endpoints.MapPost("/runScript", async context =>
            {
                var slot = int.Parse(context.Request.Query["slot"].ToString());
                Program.LoadAndStartScript(slot);
                await context.Response.WriteAsync($"Script started for slot {slot}");
            });

            // Run/start script
            endpoints.MapPost("/stopScript", async context =>
            {
                var slot = int.Parse(context.Request.Query["slot"].ToString());
                Program.StopScript(slot);
                await context.Response.WriteAsync($"Script Stopped for slot {slot}");
            });

            endpoints.MapGet("/settings", async context =>
            {
                var ishost = IsLocalhostRequest(context);
                if (!ishost) { await context.Response.WriteAsync(SimpleRedirect()); return; }
                string html = GetIndexHtml(ishost);
                await context.Response.WriteAsync(html);
            });
            endpoints.MapGet("/routing", async context =>
            {
                var ishost = IsLocalhostRequest(context);
                if (!ishost) { await context.Response.WriteAsync(SimpleRedirect()); return; }
                string html = GetIndexHtml(ishost);
                await context.Response.WriteAsync(html);
            });
            endpoints.MapGet("/parameter", async context =>
            {
                var ishost = IsLocalhostRequest(context);
                if (!ishost) { await context.Response.WriteAsync(SimpleRedirect()); return; }
                string html = GetParametersHtml();
                await context.Response.WriteAsync(html);
            });

            endpoints.MapPost("/saveState", async context =>
            {
                var slot = context.Request.Query["slot"].ToString();
                var state = new State();
                //use parametersStates to build state
                state.Toggles = parametersStates
                    .Where(kv => kv.Key.Item2 == "Bool" && !IsParameterBlocked(kv.Key.Item1, "Bool"))
                    .Select(kv => new Toggle
                    {
                        Id = kv.Key.Item1.Replace("/avatar/parameters/", ""),
                        Checked = kv.Value == 1f
                    }).ToList();

                state.Sliders = parametersStates
                    .Where(kv => (kv.Key.Item2 == "Float" || kv.Key.Item2 == "Int") && !IsParameterBlocked(kv.Key.Item1, kv.Key.Item2))
                    .Select(kv => new Slider
                    {
                        Id = kv.Key.Item1.Replace("/avatar/parameters/", ""),
                        Value = kv.Value,
                        Type = kv.Key.Item2
                    }).ToList();

                if (state != null)
                {
                    // Store the state
                    var filePath = Path.Combine("states", $"{CurrentID}_state_{slot}.json");
                    Directory.CreateDirectory("states");
                    await File.WriteAllTextAsync(filePath, JsonConvert.SerializeObject(state));
                    await context.Response.WriteAsync(JsonConvert.SerializeObject(state));
                }
                else
                {
                    await context.Response.WriteAsync("Invalid state");
                }
            });

            endpoints.MapGet("/loadState", async context =>
            {
                StopAllScript();
                var slot = context.Request.Query["slot"].ToString();
                var filePath = Path.Combine("states", $"{CurrentID}_state_{slot}.json");
                currentAvatarState = int.TryParse(slot, out var s) ? s : -1;
                
                if (File.Exists(filePath))
                {
                    var state = await File.ReadAllTextAsync(filePath);
                    var states = JsonConvert.DeserializeObject<State>(state);
                    foreach (var st in states.Toggles)
                    {
                        var param = "/avatar/parameters/" + st.Id;
                        var value = st.Checked ? "t" : "f";
                        var type = "Bool";
                        if (IsParameterBlocked(param, "Bool")) { Console.WriteLine($"Skipping blocked parameter: {param}"); continue; }

                        if (lockStates.ContainsKey((param, type)) && lockStates[(param, type)])
                        {
                            if (parametersStates[(param, type)] == 0f) value = "f";
                            else value = "t";
                            var broadcastMessage = "{\"param\":\"" + context.Request.Query["param"].ToString().Replace("/", "_").Replace("_avatar_parameters_", "") + "\",\"value\":\"" + value + "\"}";
                            await WebSocketHandler.BroadcastMessage(broadcastMessage);
                            await context.Response.WriteAsync("OSC Message Locked");
                            continue;
                        }

                        var oscSender = new UdpClient("127.0.0.1", oscPortOut);

                        parametersStates[(param, type)] = value.ToLower().Contains("t") ? 1f : 0f;

                        var message = new OscMessage();
                        message = new OscMessage(
                        address: new CoreOSC.Address(param),
                        arguments: new object[] { st.Checked ? CoreOSC.OscTrue.True : CoreOSC.OscFalse.False });

                        await oscSender.SendMessageAsync(message);

                    }
                    ;
                    foreach (var st in states.Sliders)
                    {
                        var param = "/avatar/parameters/" + st.Id.Replace("input-", "").Replace("slider-", "");
                        var value = st.Value.ToString();
                        var type = st.Type;

                        if (IsParameterBlocked(param, type)) { Console.WriteLine($"Skipping blocked parameter: {param}"); continue; }

                        if (lockStates.ContainsKey((param, type)) && lockStates[(param, type)])
                        {
                            if (float.TryParse(value, out var f))
                            {
                                if (parametersStates[(param, type)] != f)
                                {
                                    var broadcastMessage = "{\"param\":\"" + context.Request.Query["param"].ToString().Replace("/", "_").Replace("_avatar_parameters_", "") + "\",\"value\":\"" + parametersStates[(param, type)] + "\"}";
                                    await WebSocketHandler.BroadcastMessage(broadcastMessage);
                                }
                            }
                            await context.Response.WriteAsync("OSC Message Locked");
                            continue;
                        }

                        var oscSender = new UdpClient("127.0.0.1", oscPortOut);

                        if (type == "Float" && float.TryParse(value, out var floatValue))
                        {
                            parametersStates[(param, type)] = floatValue;
                            var message = new CoreOSC.OscMessage(new CoreOSC.Address(param), [floatValue]);
                            await oscSender.SendMessageAsync(message);
                        }
                        else if (type == "Int" && int.TryParse(value, out var intValue))
                        {
                            parametersStates[(param, type)] = intValue;
                            var message = new CoreOSC.OscMessage(new CoreOSC.Address(param), [intValue]);
                            await oscSender.SendMessageAsync(message);
                        }
                    }

                    await context.Response.WriteAsync(state);
                }
                else
                {
                    await context.Response.WriteAsync("State not found");
                }

                for (int i = 0; i < saveCount; i++)
                {
                    LoadAndStartScript(i);
                }
            });

            endpoints.MapGet("/update/{value}", async context =>
            {
                var value = context.Request.RouteValues["value"]?.ToString();
                await context.Response.WriteAsync("Updated");
            });
            endpoints.MapGet("/sendOsc", async context =>
            {
                var param = ToUnicodeEscape(context.Request.Query["param"].ToString());
                var value = context.Request.Query["value"].ToString();
                var type = context.Request.Query["type"].ToString();

                if (lockStates.ContainsKey((param, type)) && lockStates[(param, type)])
                {
                    if (value == "f" || value == "t")
                    {
                        if (parametersStates[(param, type)] == 0f) value = "f";
                        else value = "t";
                        var broadcastMessage = "{\"param\":\"" + context.Request.Query["param"].ToString().Replace("/", "_").Replace("_avatar_parameters_", "") + "\",\"value\":\"" + value + "\"}";
                        await WebSocketHandler.BroadcastMessage(broadcastMessage);
                    }
                    else if (float.TryParse(value, out var f))
                    {
                        if (parametersStates[(param, type)] != f)
                        {
                            var broadcastMessage = "{\"param\":\"" + context.Request.Query["param"].ToString().Replace("/", "_").Replace("_avatar_parameters_", "") + "\",\"value\":\"" + parametersStates[(param, type)] + "\"}";
                            await WebSocketHandler.BroadcastMessage(broadcastMessage);
                        }
                    }
                    await context.Response.WriteAsync("OSC Message Locked");
                    return;
                }

                var oscSender = new UdpClient("127.0.0.1", oscPortOut);

                //Console.WriteLine(param.Replace(" ", " "));

                if (type == "Float" && float.TryParse(value, out var floatValue))
                {
                    parametersStates[(param, type)] = floatValue;
                    var message = new CoreOSC.OscMessage(new CoreOSC.Address(param), [floatValue]);
                    await oscSender.SendMessageAsync(message);
                }
                else if (type == "Int" && int.TryParse(value, out var intValue))
                {
                    parametersStates[(param, type)] = intValue;
                    var message = new CoreOSC.OscMessage(new CoreOSC.Address(param), [intValue]);
                    await oscSender.SendMessageAsync(message);
                }
                else if (type == "Bool")
                {
                    parametersStates[(param, type)] = value.ToLower().Contains("t") ? 1f : 0f;

                    var message = new OscMessage();
                    if (value.ToLower().Contains("t"))
                    {
                        message = new OscMessage(
                        address: new CoreOSC.Address(param),
                        arguments: new object[] { CoreOSC.OscTrue.True });
                    }
                    else
                    {
                        message = new OscMessage(
                        address: new CoreOSC.Address(param),
                        arguments: new object[] { CoreOSC.OscFalse.False });
                    }
                    await oscSender.SendMessageAsync(message);
                }
                await context.Response.WriteAsync("OSC Message Sent " + param + " " + value + " " + type);
            });
            endpoints.MapGet("/togglelock", async context =>
            {
                var param = context.Request.Query["param"].ToString();
                var locked = context.Request.Query["locked"].ToString() == "true";
                var type = context.Request.Query["type"].ToString();
                if (lockStates.ContainsKey((param, type)))
                    lockStates[(param, type)] = locked;
                else
                    lockStates.Add((param, type), locked);
                await context.Response.WriteAsync(param + " " + type + " lock set to " + locked.ToString());
            });

            //for https certification /!\ VERY IMPORTANT /!\
            endpoints.MapGet("/.well-known/{**filePath}", async (HttpContext context, string filePath) =>
            {
                // Normalize the path
                string normalizedPath = Path.Combine(".well-known", filePath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(normalizedPath))
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Not found");
                    return;
                }

                await context.Response.SendFileAsync(normalizedPath);
            });


            // Additional endpoints can be added here...
        }
        private static string ToUnicodeEscape(string s)
        {
            if (s == null) return "";

            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c <= 127)
                {
                    // ASCII stays the same
                    sb.Append(c);
                }
                else
                {
                    // Non-ASCII becomes \uXXXX
                    sb.Append("\\u");
                    sb.Append(((int)c).ToString("X4"));
                }
            }
            return sb.ToString();
        }
        #endregion

        #region HTML & File Helpers

        // --- UpdateHtml produces a JS fragment which defines window.ParameterList and contains templates for each parameter ---
        private static string UpdateHtml(string oscValue, bool isHost)
        {
            // Locate OSC JSON folder
            var oscFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low", "VRChat", "VRChat", "OSC");
            var jsonFilePath = Directory.GetFiles(oscFolderPath, $"{oscValue}.json", SearchOption.AllDirectories).FirstOrDefault();
            if (jsonFilePath == null) { Console.WriteLine("JSON file not found."); return ""; }

            var jsonContent = File.ReadAllText(jsonFilePath);
            var json = JObject.Parse(jsonContent);
            var parameters = json["parameters"];
            var sb = new StringBuilder();

            var normalParams = new List<JToken>();
            var readOnlyParams = new List<JToken>();

            // Pass 1: sort parameters into two categories
            foreach (var param in parameters.Reverse())
            {
                if (param["output"] == null) continue;

                var name = param["name"].ToString();
                var address = param["output"]["address"].ToString();
                var id = address.Replace("/avatar/parameters/", "");
                if (id.Contains("/")) id = id.Replace("/", "_");

                var type = param["output"]["type"].ToString();
                if (IsParameterBlocked(id, type)) continue;

                bool isReadOnly =
                    (vRCDefaultParameters.ContainsKey(id) && vRCDefaultParameters[id] == type)
                    || param["input"] == null;

                if (isReadOnly)
                    readOnlyParams.Add(param);
                else
                    normalParams.Add(param);
            }

            // Merge with read-only parameters last
            var orderedParams = normalParams.Concat(readOnlyParams);

            // Start JS array
            sb.AppendLine("<script>");
            sb.AppendLine("window.ParameterList = window.ParameterList || [];");
            sb.AppendLine("window.ParameterList.length = 0;");

            // Pass 2: build HTML for all params in the new order
            foreach (var param in orderedParams)
            {
                var name = param["name"].ToString();
                var address = param["output"]["address"].ToString();
                var id = address.Replace("/avatar/parameters/", "");
                if (id.Contains("/")) id = id.Replace("/", "_");

                var type = param["output"]["type"].ToString();

                bool isReadOnly =
                    (vRCDefaultParameters.ContainsKey(id) && vRCDefaultParameters[id] == type)
                    || param["input"] == null;

                string group = ExtractGroupFromAddress(isReadOnly ?
                    param["output"]["address"].ToString().Replace("/avatar/parameters/", "/avatar/parameters/VRChat/") :
                    param["output"]["address"].ToString());

                if (!lockStates.ContainsKey((address, type))) lockStates.Add((address, type), false);
                if (!parametersStates.ContainsKey((address, type))) parametersStates.Add((address, type), 0f);

                string lockClass = lockStates[(address, type)] ? "locked" : "";

                string html = type switch
                {
                    "Bool" => CreateBoolControl(id, name, address, lockClass, isHost, parametersStates[(address, type)] == 1 ? "on" : "off", isReadOnly),
                    "Float" => CreateFloatControl(id, name, address, lockClass, isHost, parametersStates[(address, type)], isReadOnly),
                    "Int" => CreateIntControl(id, name, address, lockClass, isHost, parametersStates[(address, type)], isReadOnly),
                    _ => CreateUnknownControl(id, name, address, type, lockClass)
                };

                var jsHtml = EscapeForJs(html);

                sb.AppendLine($@"window.ParameterList.push({{
            id: '{JsEncode(id)}',
            name: '{JsEncode(name)}',
            type: '{JsEncode(type)}',
            group: '{JsEncode(group)}',
            html: `{jsHtml}`
        }});");
            }

            sb.AppendLine("</script>");
            return sb.ToString();
        }

        private static string ExtractGroupFromAddress(string address)
        {
            var key = "/avatar/parameters/";
            if (!address.StartsWith(key)) return "";
            var remainder = address.Substring(key.Length).Trim('/');
            if (!remainder.Contains("/")) return ""; // no group path
            var parts = remainder.Split('/');
            // drop last segment (param name), return joined path (can include "groups/..." etc)
            var groupParts = parts.Take(parts.Length - 1);
            return string.Join("/", groupParts);
        }
        private static string CreateBoolControl(string id, string name, string reff, string lockClass, bool isHost, string v, bool isReadOnly) => $@"
    <div class=""parameter-container"">
        <div class=""param-header"">{HtmlEncode(name)}</div>
        <button id=""{HtmlEncode(id)}"" 
                class=""bool-button {v}"" 
                {(isReadOnly ? "disabled" : $@"onclick=""toggleBool('{HtmlEncode(reff)}', this)""")}>
            OFF
        </button>
        {(isHost && !isReadOnly ?
            $@"<button id=""lock-{HtmlEncode(id)}"" class=""lock-button {lockClass}"" onclick=""toggleLock('{HtmlEncode(reff)}','Bool', this)"">Lock</button>"
            : "")}
    </div>";
        private static string CreateFloatControl(string id, string name, string reff, string lockClass, bool isHost, float v, bool isReadOnly) => $@"
    <div class=""parameter-container"">
        <div class=""param-header"">{HtmlEncode(name)}</div>
        <input id=""input-{HtmlEncode(id)}"" 
               class=""value-input"" 
               type=""number"" 
               step=""0.01"" 
               value=""{v}""
               {(isReadOnly ? "disabled" : $@"oninput=""updateValueDisplay('{HtmlEncode(id)}', this.value); sendOsc('{HtmlEncode(reff)}', this.value, 'Float')""")}>
        {(isHost && !isReadOnly ?
            $@"<button id=""lock-{HtmlEncode(id)}"" class=""lock-button {lockClass}"" onclick=""toggleLock('{HtmlEncode(reff)}','Float', this)"">Lock</button>"
            : "")}
    </div>";
        private static string CreateIntControl(string id, string name, string reff, string lockClass, bool isHost, float v, bool isReadOnly) => $@"
            <div class=""parameter-container"">
                <div class=""param-header"">{HtmlEncode(name)}</div>
                <input id=""input-{HtmlEncode(id)}"" 
                       class=""value-input"" 
                       type=""number"" 
                       step=""1"" 
                       value=""{v}""
                       {(isReadOnly ? "disabled" : $@"oninput=""updateValueDisplay('{HtmlEncode(id)}', this.value); sendOsc('{HtmlEncode(reff)}', this.value, 'Int')""")}>
                {(isHost && !isReadOnly ?
                    $@"<button id=""lock-{HtmlEncode(id)}"" class=""lock-button {lockClass}"" onclick=""toggleLock('{HtmlEncode(reff)}','Int', this)"">Lock</button>"
                    : "")}
            </div>";

        private static string CreateUnknownControl(string id, string name, string reff, string type, string lockClass) => $@"
            <div class=""parameter-container"">
                <div class=""param-header"">{HtmlEncode(name)} <small>({HtmlEncode(type)})</small></div>
                <div style=""padding:6px;"">Unsupported parameter type: {HtmlEncode(type)}</div>
            </div>";
        private static string EscapeForJs(string html)
        {
            // backticks need escaping because we use template literals; also escape ${ to avoid interpolation
            return html.Replace("\\", "\\\\").Replace("`", "\\`").Replace("${", "\\${");
        }
        private static string JsEncode(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " ");
        }
        private static string HtmlEncode(string s) { if (s == null) return ""; return System.Net.WebUtility.HtmlEncode(s); }

        private static string GenerateSaveLoadButtonsHtml()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < saveCount; i++)
            {
                sb.AppendLine($@"<div class=""button-container""><button onclick=""saveState({i})"">Save Slot #{i}</button>
                                 <button onclick=""loadState({i})"">Load Slot #{i}</button></div>");
            }
            return sb.ToString();
        }

        private static bool IsParameterBlocked(string id, string type)
        {
            string globalFile = "parameters/avatar_Global_settings.json";
            string avatarFile = $"parameters/avatar_{CurrentID}_settings.json";

            if (File.Exists(globalFile) && IsBlocked(LoadSettings(globalFile), id, type)) return true;
            if (File.Exists(avatarFile) && IsBlocked(LoadSettings(avatarFile), id, type)) return true;

            return false;
        }
        public static ParameterSettings LoadSettings(string filePath)
        {
            if (File.Exists(filePath))
            {
                return JsonConvert.DeserializeObject<ParameterSettings>(File.ReadAllText(filePath));
            }
            return new ParameterSettings();
        }
        public static bool IsBlocked(ParameterSettings settings, string id, string type)
        {
            List<string> blockList = new List<string>();
            string mode = string.Empty;

            if (type == "Bool")
            {
                blockList = GetParameterNamesByType(settings, "Bool");
                mode = settings.BoolMode;
            }
            else if (type == "Float")
            {
                blockList = GetParameterNamesByType(settings, "Float");
                mode = settings.FloatMode;
            }
            else if (type == "Int")
            {
                blockList = GetParameterNamesByType(settings, "Int");
                mode = settings.IntMode;
            }

            // Wildcard matching and checking block/whitelist mode
            foreach (var item in blockList)
            {
                if (WildcardMatch(id, item.Replace("/avatar/parameters/", "")))
                {
                    return true;  // Return true if blocked by blacklist
                }
            }

            return false; // Not blocked
        }
        public static List<string> GetParameterNamesByType(ParameterSettings settings, string type)
        {
            List<string> result = new List<string>();
            foreach (var param in settings.Parameters)
            {
                if (param.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(param.Name);
                }
            }
            return result;
        }
        public static bool WildcardMatch(string input, string pattern)
        {
            // Convert wildcard pattern to regex pattern
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
                        ProcessMessage(msg);
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

        private static void ProcessMessage(string json)
        {
            try
            {
                var jObj = JObject.Parse(json);
                var action = jObj["action"]?.ToString();

                switch (action)
                {
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
        public static async Task BroadcastMessage(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            var segment = new ArraySegment<byte>(buffer);

            List<WebSocket> disconnected = new();

            foreach (var ws in _webSockets)
            {
                if (ws.State == WebSocketState.Open)
                    await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                else
                    disconnected.Add(ws);
            }

            foreach (var ws in disconnected)
                _webSockets.Remove(ws);
        }
    }
    #endregion

    #region Data Models
    public class State { public List<Toggle> Toggles { get; set; } public List<Slider> Sliders { get; set; } }
    public class ScriptState { public string BlocklyWorkspace { get; set; } public string GeneratedScript { get; set; } public Dictionary<string, object> UserVariables { get; set; } = new Dictionary<string, object>(); }
    public class Toggle { public string Id { get; set; } public bool Checked { get; set; } }
    public class Slider { public string Id { get; set; } public float Value { get; set; } public string Type { get; set; } }
    public class AvatarList { public string[] AvatarIds { get; set; } public bool IsWhiteList { get; set; } }
    public class Parameter { public string Name { get; set; } public string Type { get; set; } }
    public class ParameterSettings { public List<Parameter> Parameters { get; set; } public string BoolMode { get; set; } public string IntMode { get; set; } public string FloatMode { get; set; } }
    public class UdpRoute { public string InputPort { get; set; } public List<UdpOutput> Outputs { get; set; } }
    public class UdpOutput { public string Ip { get; set; } public string Port { get; set; } }
    public class RoutingConfiguration { public int InputPort { get; set; } public List<Output> Outputs { get; set; } }
    public class Output { public string Ip { get; set; } public int Port { get; set; } }

    #endregion
}


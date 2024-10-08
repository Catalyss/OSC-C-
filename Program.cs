using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore;
using Newtonsoft.Json.Linq;
using System.Net.Sockets;
using CoreOSC.IO;
using CoreOSC;
using Newtonsoft.Json;
using System.Text.RegularExpressions;

namespace OscWebServer
{
    class Program
    {
        private static string RoutingHTML = "<!DOCTYPE html>\n<html lang=\"en\">\n  <head>\n    <meta charset=\"UTF-8\" />\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />\n    <title>Routing Configuration</title>\n    <style>\n      footer {\n        display: flex;\n        justify-content: center;\n        position: fixed;\n        bottom: -10px;\n        left: 0px;\n        right: 0px;\n        margin-bottom: 0px;\n        padding: 0px;\n        color: #888888;\n      }\n      body {\n        font-family: Arial, sans-serif;\n        background-color: #121212;\n        color: #e0e0e0;\n        padding: 20px; /* Added padding for better layout */\n      }\n      .section {\n        margin-bottom: 20px;\n      }\n      .list-container {\n        display: flex;\n        flex-direction: column;\n        max-height: 300px;\n        overflow-y: auto;\n        border: 1px solid #ccc;\n        padding: 10px;\n        background-color: #2e2e2e;\n      }\n      .list-buttons {\n        margin-top: 10px;\n      }\n      .list-item {\n        display: flex;\n        justify-content: space-between;\n        margin-bottom: 5px;\n      }\n      textarea {\n        width: 80%;\n        resize: none;\n        color: white;\n        background-color: #464646;\n      }\n      select {\n        width: 15%;\n        color: white;\n        background-color: #464646;\n      }\n      button {\n        margin-right: 5px;\n        background-color: #007bff;\n        color: white;\n        border: none;\n        padding: 5px 10px;\n        cursor: pointer;\n      }\n      button:hover {\n        background-color: #0056b3;\n      }\n      .route {\n        margin-bottom: 15px;\n        background-color: #2e2e2e;\n        padding: 10px;\n        border-radius: 5px;\n      }\n      .output {\n        display: flex;\n        align-items: center;\n        margin-bottom: 5px;\n      }\n      .output input {\n        margin-right: 5px;\n      }\n      #routingContainer {\n        margin-bottom: 20px;\n      }\n    </style>\n  </head>\n  <body>\n    <button onclick=\"location.href='http://INSERTPUBLICIP/settings'\">← Go Back</button> \n    <h2>Edit Routing Configuration</h2>\n    <div id=\"routingContainer\" class=\"list-container\">\n      <!-- Existing routing data will be populated here -->\n    </div>\n    <div class=\"list-buttons\">\n      <button id=\"addRoute\">Add Route</button>\n    </div>\n    <br />\n    <button id=\"saveRouting\">Save Routing</button>\n    \n    <footer>\n        <p>Current OSC avatar ID : AVATARIDPLEASEBEHERE</p>\n      </footer>\n      \n    <script>\n      // Dynamically add routing rows\n      function addRoute(inputPort = \"\", outputs = []) {\n        const routeDiv = document.createElement(\"div\");\n        routeDiv.classList.add(\"route\");\n\n        const inputPortInput = document.createElement(\"input\");\n        inputPortInput.placeholder = \"Input Port\";\n        inputPortInput.value = inputPort;\n        routeDiv.appendChild(inputPortInput);\n\n        const outputsDiv = document.createElement(\"div\");\n        outputsDiv.classList.add(\"outputs\");\n\n        // Add existing outputs\n        outputs.forEach((output) =>\n          addOutput(outputsDiv, output.ip, output.port)\n        );\n\n        const addOutputButton = document.createElement(\"button\");\n        addOutputButton.textContent = \"Add Output\";\n        addOutputButton.addEventListener(\"click\", () => addOutput(outputsDiv));\n        routeDiv.appendChild(addOutputButton);\n\n        routeDiv.appendChild(outputsDiv);\n\n        const removeRouteButton = document.createElement(\"button\");\n        removeRouteButton.textContent = \"Remove Route\";\n        removeRouteButton.addEventListener(\"click\", () => routeDiv.remove());\n        routeDiv.appendChild(removeRouteButton);\n\n        document.getElementById(\"routingContainer\").appendChild(routeDiv);\n      }\n\n      // Dynamically add output fields\n      function addOutput(outputsDiv, ip = \"\", port = \"\") {\n        const outputDiv = document.createElement(\"div\");\n        outputDiv.classList.add(\"output\");\n\n        const ipInput = document.createElement(\"input\");\n        ipInput.placeholder = \"IP Address\";\n        ipInput.value = ip;\n        outputDiv.appendChild(ipInput);\n\n        const portInput = document.createElement(\"input\");\n        portInput.placeholder = \"Port\";\n        portInput.value = port;\n        outputDiv.appendChild(portInput);\n\n        const removeOutputButton = document.createElement(\"button\");\n        removeOutputButton.textContent = \"Remove Output\";\n        removeOutputButton.addEventListener(\"click\", () => outputDiv.remove());\n        outputDiv.appendChild(removeOutputButton);\n\n        outputsDiv.appendChild(outputDiv);\n      }\n\n      // Fetch the current routing configuration on page load\n      fetch(\"/api/loadRouting\")\n        .then((response) => response.json())\n        .then((data) => {\n          data.forEach((route) => addRoute(route.inputPort, route.outputs));\n        });\n\n      // Save the routing configuration\n      document.getElementById(\"saveRouting\").addEventListener(\"click\", () => {\n        const routing = Array.from(\n          document.getElementById(\"routingContainer\").children\n        ).map((routeDiv) => {\n          const inputPort = routeDiv.querySelector(\"input\").value;\n          const outputs = Array.from(\n            routeDiv.querySelector(\".outputs\").children\n          ).map((outputDiv) => {\n            return {\n              ip: outputDiv.querySelector(\"input\").value,\n              port: outputDiv.querySelectorAll(\"input\")[1].value,\n            };\n          });\n          return { inputPort: inputPort, outputs: outputs };\n        });\n\n        fetch(\"/api/saveRouting\", {\n          method: \"POST\",\n          headers: { \"Content-Type\": \"application/json\" },\n          body: JSON.stringify(routing),\n        }).then((response) => {\n          if (response.ok) {\n            alert(\"Routing saved!\");\n          } else {\n            alert(\"Error saving routing.\");\n          }\n        });\n      });\n\n      document.getElementById(\"addRoute\").addEventListener(\"click\", () => {\n        addRoute(); // Add an empty route for the user to fill in\n      });\n    </script>\n  </body>\n</html>\n";
        private static string indexHTML = "<html><head><meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<style>\nbody { background-color: #121212; color: #e0e0e0; font-family: Arial, sans-serif; padding: 10px; }\nh1 { text-align: center; font-size: 2em; }\n.toggle { position: relative; display: inline-block; width: 60px; height: 34px; margin-bottom: 10px; }\n.toggle input { opacity: 0; width: 0; height: 0; }\n.slider { position: absolute; cursor: pointer; top: 0; left: 0; right: 0; bottom: 0; background-color: #333; transition: .4s; border-radius: 34px; }\n.slider:before { position: absolute; content: ''; height: 26px; width: 26px; border-radius: 50%; background-color: #e0e0e0; transition: .4s; }\ninput:checked + .slider { background-color: #2196F3; }\ninput:checked + .slider:before { transform: translateX(26px); }\n.range-slider { width: 100%; background-color: #333; color: #e0e0e0; margin-bottom: 10px; }\n.value-display { font-weight: bold; margin-left: 10px; }\nbutton { padding: 10px 15px; font-size: 1.2em; margin: 5px 0; background-color: #2196F3; color: white; border: none; cursor: pointer; width: 100%; }\nbutton:disabled { background-color: #555; }\n.container { display: flex; flex-wrap: wrap; gap: 10px; justify-content: space-between; }\n.button-container { display: flex; flex-direction: column; align-items: center; width: 100%; max-width: 200px; margin: 10px auto; }\n.parameter-container { border: 1px solid #e0e0e0; padding: 10px; margin-bottom: 15px; border-radius: 8px; }\n.param-header { text-align: center; font-weight: bold; font-size: 1.2em; margin-bottom: 10px; }\n.increment-buttons { text-align: center; margin-top: 10px; }\n.button-row { display: flex; justify-content: center; gap: 5px; margin-bottom: 5px; }\n.bool-button { background-color: #333; color: #e0e0e0; border: none; padding: 15px; cursor: pointer; width: 100%; text-align: center; border-radius: 8px; font-size: 1.2em; }\n.bool-button.on { background-color: #2196F3; color: white; }\n.bool-button.off { background-color: #555; color: #e0e0e0; }\nfooter {\n  display: flex;\n  justify-content: center;\n  position: fixed;\n  bottom: -10px;\n  left: 0px;\n  right: 0px;\n  margin-bottom: 0px;\n  padding: 0px;\n  color: #888888;\n}\n</style></head><body>\n<h1>OSC Remote Control Panel</h1>\n  <button onclick=\"location.href='http://PUBLICIPGOESHERE/settings'\">Setting page</button>\n<div class=\"container\">\n</div>\n\n<script>\n  var ws = new WebSocket(`ws://PUBLICIPGOESHERE/ws`);\n\n  ws.onopen = function() {\n    console.log('WebSocket connection established.');\n  };\n\n  ws.onclose = function() {\n    console.log('WebSocket connection closed.');\n  };\n\n  ws.onmessage = function(event) {\n    var data = JSON.parse(event.data);\n\n    if (data.action === 'updateHtml') {\n      // Update the entire HTML content\n      document.open();\n      document.write(decodeURIComponent(data.html));\n      document.close();\n    } else if (data.param) {\n      var param = data.param;\n      var value = data.value;\n      var slider = document.getElementById(`slider-${param}`);\n      var toggleButton = document.getElementById(`${param}`);\n\n      // Update slider values\n      if (slider) {\n        slider.value = value;\n        updateValueDisplay(param, value);\n      }\n\n      // Update toggle button states\n      if (toggleButton) {\n        var isOn = value === 't';\n        toggleButton.classList.toggle('on', !isOn);\n        toggleButton.classList.toggle('off', isOn);\n        toggleBool(param, toggleButton)\n      }\n    }\n  };\n</script>\n<footer>\n  <p>Current OSC avatar ID : no avatar loaded</p>\n</footer>\n</body></html>\n";
        private static string avatarsettingsHTML = "<!DOCTYPE html>\n<html lang=\"en\">\n  <head>\n    <meta charset=\"UTF-8\" />\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />\n    <title>Edit AvatarID Parameters</title>\n    <style>\n        \n      footer {\n        display: flex;\n        justify-content: center;\n        position: fixed;\n        bottom: -10px;\n        left: 0px;\n        right: 0px;\n        margin-bottom: 0px;\n        padding: 0px;\n        color: #888888;\n      }\n      body {\n        font-family: Arial, sans-serif;\n        background-color: #121212;\n        color: #e0e0e0;\n      }\n      .section {\n        margin-bottom: 20px;\n      }\n      .list-container {\n        display: flex;\n        flex-direction: column;\n        max-height: 300px;\n        overflow-y: auto;\n        border: 1px solid #ccc;\n        padding: 10px;\n        background-color: #2e2e2e;\n      }\n      .list-buttons {\n        margin-top: 10px;\n      }\n      .list-item {\n        display: flex;\n        justify-content: space-between;\n        margin-bottom: 5px;\n      }\n      textarea {\n        width: 80%;\n        resize: none;\n        color: white;\n        background-color: #464646;\n      }\n      select {\n        width: 15%;\n        color: white;\n        background-color: #464646;\n      }\n      button {\n        margin-right: 5px;\n        background-color: #007bff;\n        color: white;\n        border: none;\n        padding: 5px 10px;\n        cursor: pointer;\n      }\n      button:hover {\n        background-color: #0056b3;\n      }\n    </style>\n  </head>\n  <body>\n    <button onclick=\"location.href='http://INSERTPUBLICIP/settings'\">← Go Back</button> <h1>Edit Parameters for AvatarID: {{AVATARID}}</h1>\n\n    <div class=\"section\">\n      <h2>Per-Avatar Parameter List</h2>\n      <div class=\"list-container\" id=\"avatarParamList\">\n        <!-- List of parameters for this AvatarID will go here -->\n      </div>\n      <div class=\"list-buttons\">\n        <button id=\"addAvatarParam\">+</button>\n        <button id=\"toggleBoolMode\">Switch Bool to Whitelist</button>\n        <button id=\"toggleIntMode\">Switch Int to Whitelist</button>\n        <button id=\"toggleFloatMode\">Switch Float to Whitelist</button>\n      </div>\n    </div>\n\n    <button id=\"saveAvatarSettings\">Save Settings</button>\n    <button id=\"deletedAvatarSettings\">Delete Settings</button>\n\n    <footer>\n        <p>Current OSC avatar ID : AVATARIDPLEASEBEHERE</p>\n      </footer>\n    <script>\n      const avatarId = \"{{AVATARID}}\"; // AvatarID dynamically set from URL\n      // Toggle between blacklist and whitelist for AvatarID\n      let boolMode = \"Whitelist\";\n      let intMode = \"Whitelist\";\n      let floatMode = \"Whitelist\";\n\n      // Toggle Bool Mode\n      document\n        .getElementById(\"toggleBoolMode\")\n        .addEventListener(\"click\", () => {\n          const button = document.getElementById(\"toggleBoolMode\");\n          boolMode = boolMode === \"Whitelist\" ? \"Blacklist\" : \"Whitelist\";\n          button.textContent = `Switch Bool to ${boolMode}`;\n        });\n\n      // Toggle Int Mode\n      document.getElementById(\"toggleIntMode\").addEventListener(\"click\", () => {\n        const button = document.getElementById(\"toggleIntMode\");\n        intMode = intMode === \"Whitelist\" ? \"Blacklist\" : \"Whitelist\";\n        button.textContent = `Switch Int to ${intMode}`;\n      });\n\n      // Toggle Float Mode\n      document\n        .getElementById(\"toggleFloatMode\")\n        .addEventListener(\"click\", () => {\n          const button = document.getElementById(\"toggleFloatMode\");\n          floatMode = floatMode === \"Whitelist\" ? \"Blacklist\" : \"Whitelist\";\n          button.textContent = `Switch Float to ${floatMode}`;\n        });\n\n      // Load settings for this AvatarID on page load\n      fetch(`/api/loadAvatarSettings/${avatarId}`)\n        .then((response) => response.json())\n        .then((data) => {\n          const list = document.getElementById(\"avatarParamList\");\n          data.parameters.forEach((param) => {\n            addParamToList(list, param.name, param.type);\n          });\n          // Set the toggle states based on loaded data\n          if (data.boolMode) {\n            boolMode = data.boolMode;\n            document.getElementById(\n              \"toggleBoolMode\"\n            ).textContent = `Switch Bool to ${boolMode}`;\n          }\n          if (data.intMode) {\n            intMode = data.intMode;\n            document.getElementById(\n              \"toggleIntMode\"\n            ).textContent = `Switch Int to ${intMode}`;\n          }\n          if (data.floatMode) {\n            floatMode = data.floatMode;\n            document.getElementById(\n              \"toggleFloatMode\"\n            ).textContent = `Switch Float to ${floatMode}`;\n          }\n        });\n\n      // Functionality to add a new parameter\n      document\n        .getElementById(\"addAvatarParam\")\n        .addEventListener(\"click\", () => {\n          const list = document.getElementById(\"avatarParamList\");\n          addParamToList(list, \"\", \"Bool\");\n        });\n      function addParamToList(list, paramName, paramType) {\n        const listItem = document.createElement(\"div\");\n        listItem.classList.add(\"list-item\");\n\n        // Textarea for parameter name\n        const textarea = document.createElement(\"textarea\");\n        textarea.placeholder = \"Enter Parameter Name...\";\n        textarea.value = paramName;\n        listItem.appendChild(textarea);\n\n        // Dropdown for parameter type\n        const select = document.createElement(\"select\");\n        const options = [\"Bool\", \"Float\", \"Int\"];\n        options.forEach((opt) => {\n          const optionElement = document.createElement(\"option\");\n          optionElement.value = opt;\n          optionElement.textContent = opt;\n          if (opt === paramType) {\n            optionElement.selected = true;\n          }\n          select.appendChild(optionElement);\n        });\n        listItem.appendChild(select);\n\n        // Remove button\n        const removeButton = document.createElement(\"button\");\n        removeButton.textContent = \"X   \";\n        removeButton.addEventListener(\"click\", () => {\n          list.removeChild(listItem); // Remove the parameter from the list\n        });\n        listItem.appendChild(removeButton);\n\n        // Append the complete list item to the list\n        list.appendChild(listItem);\n      }\n\n      // Save settings\n      document\n        .getElementById(\"saveAvatarSettings\")\n        .addEventListener(\"click\", () => {\n          const list = document.getElementById(\"avatarParamList\");\n          const parameters = Array.from(list.children).map((item) => {\n            return {\n              name: item.querySelector(\"textarea\").value,\n              type: item.querySelector(\"select\").value,\n            };\n          });\n\n          const data = {\n            parameters,\n            boolMode, // Add current Bool mode\n            intMode, // Add current Int mode\n            floatMode, // Add current Float mode\n          };\n\n          fetch(`/api/saveAvatarSettings/${avatarId}`, {\n            method: \"POST\",\n            headers: { \"Content-Type\": \"application/json\" },\n            body: JSON.stringify(data),\n          }).then((response) => {\n            if (response.ok) {\n              alert(\"Settings saved!\");\n            } else {\n              alert(\"Error saving settings.\");\n            }\n          });\n        });\n\n      document\n        .getElementById(\"deletedAvatarSettings\")\n        .addEventListener(\"click\", () => {\n          fetch(`/api/delete/${avatarId}`, {\n            method: \"POST\",\n          })\n            .then((response) => {\n              if (response.ok) {\n                // Check if the response status is OK\n                window.location.href = \"/settings\"; // Redirect to Settings.html\n              } else {\n                alert(\"Error deleting AvatarID.\");\n              }\n            })\n            .catch((error) => {\n              console.error(\"Fetch error:\", error);\n              alert(\"An error occurred while trying to delete the AvatarID.\");\n            });\n        });\n    </script>\n  </body>\n</html>\n";
        private static string SettingsHTML = "<!DOCTYPE html>\n<html lang=\"en\">\n  <head>\n    <meta charset=\"UTF-8\" />\n    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />\n    <title>Settings</title>\n    <style>\n      footer {\n        display: flex;\n        justify-content: center;\n        position: fixed;\n        bottom: -10px;\n        left: 0px;\n        right: 0px;\n        margin-bottom: 0px;\n        padding: 0px;\n        color: #888888;\n      }\n      body {\n        font-family: Arial, sans-serif;\n        background-color: #121212;\n        color: #e0e0e0;\n      }\n      .section {\n        margin-bottom: 20px;\n      }\n      .list-container {\n        display: flex;\n        flex-direction: column;\n        max-height: 300px;\n        overflow-y: auto;\n        border: 1px solid #ccc;\n        padding: 10px;\n        background-color: #2e2e2e;\n      }\n      .list-buttons {\n        margin-top: 10px;\n      }\n      .list-item {\n        display: flex;\n        justify-content: space-between;\n        margin-bottom: 5px;\n      }\n      textarea {\n        width: 80%;\n        resize: none;\n        color: white;\n        background-color: #464646;\n      }\n      select {\n        width: 15%;\n        color: white;\n        background-color: #464646;\n      }\n      button {\n        margin-right: 5px;\n        background-color: #007bff;\n        color: white;\n        border: none;\n        padding: 5px 10px;\n        cursor: pointer;\n      }\n      button:hover {\n        background-color: #0056b3;\n      }\n    </style>\n  </head>\n  <body>\n    <button onclick=\"location.href='http://INSERTPUBLICIP/'\">← Go Back</button>\n    <button onclick=\"location.href='http://INSERTPUBLICIP/settings/routing'\">→ Routing Panel</button>\n    <h1>Settings</h1>\n\n    <!-- AvatarID Blacklist/Whitelist -->\n    <div class=\"section\">\n      <h2>AvatarID List</h2>\n      <div class=\"list-container\" id=\"avatarIdList\">\n        <!-- List of Avatar IDs will go here -->\n      </div>\n      <div class=\"list-buttons\">\n        <button id=\"addAvatarId\">+</button>\n        <button id=\"toggleAvatarMode\">Switch to Whitelist</button>\n      </div>\n    </div>\n\n    <button id=\"saveIDSettings\">Save AvatarID Settings</button>\n\n    <!-- Global Parameter List -->\n    <div class=\"section\">\n      <h2>Global Parameter List</h2>\n      <div class=\"list-container\" id=\"avatarParamList\">\n        <!-- Global Parameter List will go here -->\n      </div>\n      <div class=\"list-buttons\">\n        <button id=\"addAvatarParam\">+</button>\n        <button id=\"toggleBoolMode\">Switch Bool to Whitelist</button>\n        <button id=\"toggleIntMode\">Switch Int to Whitelist</button>\n        <button id=\"toggleFloatMode\">Switch Float to Whitelist</button>\n      </div>\n    </div>\n\n    <button id=\"saveAvatarSettings\">Save Global Parameter Settings</button>\n\n    <!-- Other Pages Listing -->\n    <div class=\"section\">\n      <h2>Avatar File Listing</h2>\n      <div class=\"list-container\" id=\"avatarParamList\">\n        <!-- List of parameters for this AvatarID will go here -->\n      </div>\n    </div>\n\n    <footer>\n      <p>Current OSC avatar ID : AVATARIDPLEASEBEHERE</p>\n    </footer>\n    <script>\n      // Functionality to handle adding and removing items for AvatarID\n      document.getElementById(\"addAvatarId\").addEventListener(\"click\", () => {\n        const list = document.getElementById(\"avatarIdList\");\n        const listItem = document.createElement(\"div\");\n        listItem.classList.add(\"list-item\");\n\n        const textarea = document.createElement(\"textarea\");\n        textarea.placeholder = \"Enter AvatarID...\";\n        listItem.appendChild(textarea);\n\n        const openParamsButton = document.createElement(\"button\");\n        openParamsButton.textContent = \"Edit Params\";\n        openParamsButton.addEventListener(\"click\", () => {\n          const avatarId = textarea.value; // Use the typed AvatarID from the textarea\n          if (avatarId) {\n            window.location.href = `/settings/${avatarId}`; // Navigate to /settings/AVATARID\n          } else {\n            alert(\"Please enter a valid AvatarID.\");\n          }\n        });\n        listItem.appendChild(openParamsButton);\n\n        // Add remove button for each AvatarID\n        const removeButton = document.createElement(\"button\");\n        removeButton.textContent = \"-\";\n        removeButton.addEventListener(\"click\", () => {\n          list.removeChild(listItem); // Remove the AvatarID from the list\n        });\n        listItem.appendChild(removeButton);\n\n        list.appendChild(listItem);\n      });\n\n      let isWhiteList = true; // Initialize as true, assuming it starts in whitelist mode\n      function addAvatarIdToList(list, avatarId) {\n        const listItem = document.createElement(\"div\");\n        listItem.classList.add(\"list-item\");\n\n        const textarea = document.createElement(\"textarea\");\n        textarea.placeholder = \"Enter AvatarID...\";\n        textarea.value = avatarId; // Set the value to the existing AvatarID\n        listItem.appendChild(textarea);\n\n        const openParamsButton = document.createElement(\"button\");\n        openParamsButton.textContent = \"Edit Params\";\n        openParamsButton.addEventListener(\"click\", () => {\n          const avatarId = textarea.value; // Use the typed AvatarID from the textarea\n          if (avatarId) {\n            window.location.href = `/settings/${avatarId}`; // Navigate to /settings/AVATARID\n          } else {\n            alert(\"Please enter a valid AvatarID.\");\n          }\n        });\n        listItem.appendChild(openParamsButton);\n\n        // Add remove button for each AvatarID\n        const removeButton = document.createElement(\"button\");\n        removeButton.textContent = \"-\";\n        removeButton.addEventListener(\"click\", () => {\n          list.removeChild(listItem); // Remove the AvatarID from the list\n        });\n        listItem.appendChild(removeButton);\n\n        list.appendChild(listItem);\n      }\n\n      // Toggle between blacklist and whitelist for AvatarID\n      document\n        .getElementById(\"toggleAvatarMode\")\n        .addEventListener(\"click\", () => {\n          const button = document.getElementById(\"toggleAvatarMode\");\n          isWhiteList = !isWhiteList; // Toggle the boolean value\n          button.textContent = isWhiteList\n            ? \"Switch to Blacklist\"\n            : \"Switch to Whitelist\";\n        });\n\n      // Load AvatarID List on page load\n      fetch(\"/api/loadAvatarListing\")\n        .then((response) => response.json())\n        .then((data) => {\n          const list = document.getElementById(\"avatarIdList\");\n          data.avatarIds.forEach((avatarId) => {\n            addAvatarIdToList(list, avatarId);\n          });\n          isWhiteList = data.IsWhiteList || true; // Set the initial state from loaded data\n          document.getElementById(\"toggleAvatarMode\").textContent = isWhiteList\n            ? \"Switch to Blacklist\"\n            : \"Switch to Whitelist\"; // Update button text\n        });\n\n      document\n        .getElementById(\"saveIDSettings\")\n        .addEventListener(\"click\", () => {\n          const list = document.getElementById(\"avatarIdList\");\n          const avatarIds = Array.from(list.children).map((item) => {\n            return item.querySelector(\"textarea\").value;\n          });\n\n          const data = {\n            avatarIds,\n            IsWhiteList: isWhiteList, // Include the boolean value\n          };\n\n          fetch(\"/api/saveAvatarListing\", {\n            method: \"POST\",\n            headers: { \"Content-Type\": \"application/json\" },\n            body: JSON.stringify(data),\n          }).then((response) => {\n            if (response.ok) {\n              alert(\"AvatarID list saved!\");\n            } else {\n              alert(\"Error saving AvatarID list.\");\n            }\n          });\n        });\n\n      let boolMode = \"Whitelist\";\n      let intMode = \"Whitelist\";\n      let floatMode = \"Whitelist\";\n\n      // Toggle Bool Mode\n      document\n        .getElementById(\"toggleBoolMode\")\n        .addEventListener(\"click\", () => {\n          const button = document.getElementById(\"toggleBoolMode\");\n          boolMode = boolMode === \"Whitelist\" ? \"Blacklist\" : \"Whitelist\";\n          button.textContent = `Switch Bool to ${boolMode}`;\n        });\n\n      // Toggle Int Mode\n      document.getElementById(\"toggleIntMode\").addEventListener(\"click\", () => {\n        const button = document.getElementById(\"toggleIntMode\");\n        intMode = intMode === \"Whitelist\" ? \"Blacklist\" : \"Whitelist\";\n        button.textContent = `Switch Int to ${intMode}`;\n      });\n\n      // Toggle Float Mode\n      document\n        .getElementById(\"toggleFloatMode\")\n        .addEventListener(\"click\", () => {\n          const button = document.getElementById(\"toggleFloatMode\");\n          floatMode = floatMode === \"Whitelist\" ? \"Blacklist\" : \"Whitelist\";\n          button.textContent = `Switch Float to ${floatMode}`;\n        });\n\n      // Load settings for this AvatarID on page load\n      fetch(`/api/loadAvatarSettings/Global`)\n        .then((response) => response.json())\n        .then((data) => {\n          const list = document.getElementById(\"avatarParamList\");\n          data.parameters.forEach((param) => {\n            addParamToList(list, param.name, param.type);\n          });\n          // Set the toggle states based on loaded data\n          if (data.boolMode) {\n            boolMode = data.boolMode;\n            document.getElementById(\n              \"toggleBoolMode\"\n            ).textContent = `Switch Bool to ${boolMode}`;\n          }\n          if (data.intMode) {\n            intMode = data.intMode;\n            document.getElementById(\n              \"toggleIntMode\"\n            ).textContent = `Switch Int to ${intMode}`;\n          }\n          if (data.floatMode) {\n            floatMode = data.floatMode;\n            document.getElementById(\n              \"toggleFloatMode\"\n            ).textContent = `Switch Float to ${floatMode}`;\n          }\n        });\n\n      // Functionality to add a new parameter\n      document\n        .getElementById(\"addAvatarParam\")\n        .addEventListener(\"click\", () => {\n          const list = document.getElementById(\"avatarParamList\");\n          addParamToList(list, \"\", \"Bool\");\n        });\n      function addParamToList(list, paramName, paramType) {\n        const listItem = document.createElement(\"div\");\n        listItem.classList.add(\"list-item\");\n\n        // Textarea for parameter name\n        const textarea = document.createElement(\"textarea\");\n        textarea.placeholder = \"Enter Parameter Name...\";\n        textarea.value = paramName;\n        listItem.appendChild(textarea);\n\n        // Dropdown for parameter type\n        const select = document.createElement(\"select\");\n        const options = [\"Bool\", \"Float\", \"Int\"];\n        options.forEach((opt) => {\n          const optionElement = document.createElement(\"option\");\n          optionElement.value = opt;\n          optionElement.textContent = opt;\n          if (opt === paramType) {\n            optionElement.selected = true;\n          }\n          select.appendChild(optionElement);\n        });\n        listItem.appendChild(select);\n\n        // Remove button\n        const removeButton = document.createElement(\"button\");\n        removeButton.textContent = \"X   \";\n        removeButton.addEventListener(\"click\", () => {\n          list.removeChild(listItem); // Remove the parameter from the list\n        });\n        listItem.appendChild(removeButton);\n\n        // Append the complete list item to the list\n        list.appendChild(listItem);\n      }\n\n      // Save settings\n      document\n        .getElementById(\"saveAvatarSettings\")\n        .addEventListener(\"click\", () => {\n          const list = document.getElementById(\"avatarParamList\");\n          const parameters = Array.from(list.children).map((item) => {\n            return {\n              name: item.querySelector(\"textarea\").value,\n              type: item.querySelector(\"select\").value,\n            };\n          });\n\n          const data = {\n            parameters,\n            boolMode, // Add current Bool mode\n            intMode, // Add current Int mode\n            floatMode, // Add current Float mode\n          };\n\n          fetch(`/api/saveAvatarSettings/Global`, {\n            method: \"POST\",\n            headers: { \"Content-Type\": \"application/json\" },\n            body: JSON.stringify(data),\n          }).then((response) => {\n            if (response.ok) {\n              alert(\"Settings saved!\");\n            } else {\n              alert(\"Error saving settings.\");\n            }\n          });\n        });\n    </script>\n  </body>\n</html>\n";
        private static int webPort = 80;
        private static int oscPortOut = 9002;
        private static int oscPortIn = 9003;
        private static string CurrentID = "";
        private static IWebHost webHost;
        private static string PUBIP = "localhost";
        static async Task Main(string[] args)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // Make a GET request to the API service to get the public IP address
                    PUBIP = await client.GetStringAsync("https://api.ipify.org");
                    Console.WriteLine($"Your public IP address is: {PUBIP}");
                }
            }
            catch (Exception ex)
            {
                PUBIP = "localhost";
                Console.WriteLine($"An error occurred: {ex.Message}");
            }

            ParseArguments(args);
            StartOscListener();
            StartOscSender();
            Router("parameters/Routing.json");
            StartWebServer();
        }
        public static void Router(string configFilePath)
        {
            // Load routing config from JSON
            var routes = LoadRoutingConfig(configFilePath);
            if (routes == null)
            {
                Console.WriteLine("Failed to load routing configuration.");
                return;
            }

            // Iterate over each route and start the async UDP listener and sender
            foreach (var route in routes)
            {
                int inputPort = ResolvePort(route.InputPort);  // Convert inputPort from string to int
                if (inputPort <= 0)
                {
                    Console.WriteLine($"Invalid input port for route: {route.InputPort}");
                    continue;
                }

                var udpClient = new UdpClient(inputPort);
                Task.Run(async () =>
                {
                    while (true)
                    {
                        var packet = await udpClient.ReceiveMessageAsync();

                        // Send to all outputs for this route
                        foreach (var output in route.Outputs)
                        {
                            int outputPort = ResolvePort(output.Port);
                            if (outputPort > 0)
                            {
                                using (var senderClient = new UdpClient(output.Ip, outputPort))
                                {
                                    await senderClient.SendMessageAsync(packet);
                                }
                            }
                        }
                    }
                });
            }
        }

        // Load routing configuration from JSON
        public static List<UdpRoute> LoadRoutingConfig(string filePath)
        {
            if (File.Exists(filePath))
            {
                string jsonContent = File.ReadAllText(filePath);
                return JsonConvert.DeserializeObject<List<UdpRoute>>(jsonContent);
            }
            return null;
        }

        // Helper to resolve ports, allowing string placeholders like "oscPortIn"
        public static int ResolvePort(string port)
        {
            // Map the port strings to actual integers
            switch (port)
            {
                case "oscPortIn":
                    return 9002;  // Replace with your actual oscPortIn value
                case "oscPortOut":
                    return 9003;  // Replace with your actual oscPortOut value
                default:
                    if (int.TryParse(port, out int numericPort))
                        return numericPort;
                    return -1;  // Return -1 if the port is not a valid integer or known placeholder
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
            // Console.WriteLine("Sender Started");
        }
        private static void ParseArguments(string[] args)
        {
            foreach (var arg in args)
            {
                var parts = arg.Split(' ');
                if (parts.Length == 2)
                {
                    switch (parts[0])
                    {
                        case "-WebPort":
                            webPort = int.Parse(parts[1]);
                            break;
                        case "-OSCPortOUT":
                            oscPortOut = int.Parse(parts[1]);
                            break;
                        case "-OSCPortIN":
                            oscPortIn = int.Parse(parts[1]);
                            break;
                    }
                }
            }
        }

        private static void StartWebServer()
        {
            webHost = WebHost.CreateDefaultBuilder()
                .ConfigureServices(services => services.AddRouting())
                .Configure(app =>
                {
                    app.UseWebSockets();
                    app.Use(async (context, next) =>
                    {
                        if (context.Request.Path == "/ws")
                        {
                            //Console.WriteLine("HEY THIS GOT TRIGGERD");
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
                        else
                        {
                            await next();
                        }
                    });
                    // Handle 404 errors
                    app.Use(async (context, next) =>
                    {
                        await next.Invoke();
                        if (context.Response.StatusCode == 404)
                        {
                            context.Response.ContentType = "text/html";
                            await context.Response.WriteAsync("Access denied.");
                        }
                    });


                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/", async context =>
                        {
                            string indexhtmls = indexHTML;
                            if (context.Connection.RemoteIpAddress.ToString().Equals("::ffff:127.0.0.1") || context.Connection.RemoteIpAddress.ToString().Equals("::ffff:" + PUBIP))
                            {

                            }
                            else
                            {
                                indexhtmls = indexhtmls.Replace($"<button onclick=\"location.href=\'http://{PUBIP}/settings\'\">Setting page</button>", "");
                            }
                            indexhtmls = indexhtmls.Replace("PUBLICIPGOESHERE",PUBIP);
                            await context.Response.WriteAsync(indexhtmls);
                        });

                        endpoints.MapPost("/saveState", async context =>
                        {
                            var slot = context.Request.Query["slot"].ToString();
                            var state = await context.Request.ReadFromJsonAsync<State>();

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
                            var slot = context.Request.Query["slot"].ToString();
                            var filePath = Path.Combine("states", $"{CurrentID}_state_{slot}.json");

                            if (File.Exists(filePath))
                            {
                                var state = await File.ReadAllTextAsync(filePath);
                                await context.Response.WriteAsync(state);
                            }
                            else
                            {
                                await context.Response.WriteAsync("State not found");
                            }
                        });

                        // Dynamic AvatarID Page
                        endpoints.MapGet("/settings/routing", async context =>
                        {

                            if (context.Connection.RemoteIpAddress.ToString().Equals("::ffff:127.0.0.1") || context.Connection.RemoteIpAddress.ToString().Equals("::ffff:" + PUBIP))
                            {
                                var IDS = CurrentID;
                                if (CurrentID == "") IDS = "no avatar loaded";
                                var avatarId = (string)context.Request.RouteValues["avatarId"];
                                string avatarPageHtml = RoutingHTML
                                    .Replace("{{AVATARID}}", avatarId)
                                    .Replace("INSERTPUBLICIP", PUBIP)
                                    .Replace("AVATARIDPLEASEBEHERE", IDS);  // Replace placeholder with actual AvatarID
                                await context.Response.WriteAsync(avatarPageHtml);
                                return;

                            }

                            context.Response.StatusCode = 403; // Forbidden
                            await context.Response.WriteAsync("Access denied.");
                        });

                        endpoints.MapPost("/api/saveRouting", async (HttpContext context) =>
                        {
                            if (context.Connection.RemoteIpAddress.ToString().Equals("::ffff:127.0.0.1") || context.Connection.RemoteIpAddress.ToString().Equals("::ffff:" + PUBIP))
                            {
                                var routingConfig = await context.Request.ReadFromJsonAsync<List<RoutingConfiguration>>();
                                var jsonString = JsonConvert.SerializeObject(routingConfig, Formatting.Indented);
                                var routingJsonPath = "parameters/Routing.json";
                                await File.WriteAllTextAsync(routingJsonPath, jsonString);
                                return Results.Ok();
                            }

                            context.Response.StatusCode = 403; // Forbidden
                            await context.Response.WriteAsync("Access denied.");
                            return Results.Forbid();
                        });

                        endpoints.MapGet("/api/loadRouting", async (HttpContext context) =>
                        {
                            if (context.Connection.RemoteIpAddress.ToString().Equals("::ffff:127.0.0.1") || context.Connection.RemoteIpAddress.ToString().Equals("::ffff:" + PUBIP))
                            {
                                var routingJsonPath = "parameters/Routing.json";
                                var jsonString = await File.ReadAllTextAsync(routingJsonPath);
                                var routingConfig = JsonConvert.DeserializeObject<List<RoutingConfiguration>>(jsonString);
                                return Results.Json(routingConfig);
                            }
                            context.Response.StatusCode = 403; // Forbidden
                            await context.Response.WriteAsync("Access denied.");
                            return Results.Forbid();
                        });


                        // New Settings Page (localhost only)
                        endpoints.MapGet("/settings", async context =>
                        {
                            if (context.Connection.RemoteIpAddress.ToString().Equals("::ffff:127.0.0.1") || context.Connection.RemoteIpAddress.ToString().Equals("::ffff:" + PUBIP))
                            {
                                var IDS = CurrentID;
                                if (CurrentID == "") IDS = "no avatar loaded";
                                string settingsHtml = SettingsHTML.Replace("INSERTPUBLICIP", PUBIP).Replace("AVATARIDPLEASEBEHERE", IDS);
                                if(Directory.Exists("parameters")){
                                DirectoryInfo taskDirectory = new DirectoryInfo("parameters");
                                FileInfo[] taskFiles = taskDirectory.GetFiles("avatar_*");
                                foreach (FileInfo item in taskFiles)
                                {
                                    if (item.Name.Contains("avatar_Global_settings")) continue;

                                    string name = item.Name.ToString().Split("avatar_")[1].Split("_settings.json")[0];
                                    settingsHtml = settingsHtml.Replace("<!-- List of parameters for this AvatarID will go here -->",
                                                         "<!-- List of parameters for this AvatarID will go here -->\n"
                                    + $"<button onclick=\"location.href=\'http://{PUBIP}/settings/{name}\'\">{name}</button>"
                                    );
                                }
                                }
                                await context.Response.WriteAsync(settingsHtml);
                                return;
                            }
                            else
                            {
                                context.Response.StatusCode = 403; // Forbidden
                                await context.Response.WriteAsync("Access denied.");
                            }
                        });

                        // Handle API requests for saving/loading settings
                        endpoints.MapPost("/api/saveSettings", async context =>
                        {
                            if (context.Connection.RemoteIpAddress.ToString().Equals("::ffff:127.0.0.1") || context.Connection.RemoteIpAddress.ToString().Equals("::ffff:" + PUBIP))
                            {
                                using (StreamReader reader = new StreamReader(context.Request.Body))
                                {
                                    var content = await reader.ReadToEndAsync();
                                    Directory.CreateDirectory("parameters");
                                    File.WriteAllText("parameters/" + "settings.json", content);
                                    context.Response.StatusCode = 200; // OK
                                }

                                return;
                            }
                            context.Response.StatusCode = 403; // Forbidden
                            await context.Response.WriteAsync("Access denied.");
                        });

                        endpoints.MapPost("/api/loadSettings", async context =>
                        {
                            if (context.Connection.RemoteIpAddress.ToString().Equals("::ffff:127.0.0.1") || context.Connection.RemoteIpAddress.ToString().Equals("::ffff:" + PUBIP))
                            {
                                var settings = File.ReadAllText("parameters/" + "settings.json");
                                await context.Response.WriteAsync(settings);
                                return;
                            }
                            context.Response.StatusCode = 403; // Forbidden
                            await context.Response.WriteAsync("Access denied.");
                        });

                        // Dynamic AvatarID Page
                        endpoints.MapGet("/settings/{avatarId}", async context =>
                        {

                            if (context.Connection.RemoteIpAddress.ToString().Equals("::ffff:127.0.0.1") || context.Connection.RemoteIpAddress.ToString().Equals("::ffff:" + PUBIP))
                            {
                                var IDS = CurrentID;
                                if (CurrentID == "") IDS = "no avatar loaded";
                                var avatarId = (string)context.Request.RouteValues["avatarId"];
                                string avatarPageHtml = avatarsettingsHTML
                                    .Replace("{{AVATARID}}", avatarId)
                                    .Replace("INSERTPUBLICIP", PUBIP)
                                    .Replace("AVATARIDPLEASEBEHERE", IDS);  // Replace placeholder with actual AvatarID
                                await context.Response.WriteAsync(avatarPageHtml);
                                return;

                            }

                            context.Response.StatusCode = 403; // Forbidden
                            await context.Response.WriteAsync("Access denied.");
                        });

                        /// API to save settings for a specific AvatarID
                        endpoints.MapPost("/api/saveAvatarSettings/{avatarId}", async context =>
                        {
                            if (context.Connection.RemoteIpAddress.ToString().Equals("::ffff:127.0.0.1") || context.Connection.RemoteIpAddress.ToString().Equals("::ffff:" + PUBIP))
                            {
                                var avatarId = (string)context.Request.RouteValues["avatarId"];
                                using (StreamReader reader = new StreamReader(context.Request.Body))
                                {
                                    var content = await reader.ReadToEndAsync();
                                    Directory.CreateDirectory("parameters");
                                    File.WriteAllText("parameters/" + $"avatar_{avatarId}_settings.json", content);  // Save avatar-specific settings
                                    context.Response.StatusCode = 200; // OK
                                }
                                return;
                            }

                            context.Response.StatusCode = 403; // Forbidden
                            await context.Response.WriteAsync("Access denied.");
                        });
                        /// API to delete settings for a specific AvatarID
                        endpoints.MapPost("/api/delete/{avatarId}", async context =>
                        {
                            if (context.Connection.RemoteIpAddress.ToString().Equals("::ffff:127.0.0.1") || context.Connection.RemoteIpAddress.ToString().Equals("::ffff:" + PUBIP))
                            {
                                var avatarId = (string)context.Request.RouteValues["avatarId"];
                                using (StreamReader reader = new StreamReader(context.Request.Body))
                                {
                                    var content = await reader.ReadToEndAsync();
                                    Directory.CreateDirectory("parameters");
                                    File.Delete("parameters/" + $"avatar_{avatarId}_settings.json");  // delete avatar-specific settings
                                    context.Response.StatusCode = 200; // OK
                                    return; // Exit the function after sending the redirect
                                }
                            }

                            context.Response.StatusCode = 403; // Forbidden
                            await context.Response.WriteAsync("Access denied.");
                        });


                        // API to load settings for a specific AvatarID
                        endpoints.MapGet("/api/loadAvatarSettings/{avatarId}", async context =>
                        {
                            if (context.Connection.RemoteIpAddress.ToString().Equals("::ffff:127.0.0.1") || context.Connection.RemoteIpAddress.ToString().Equals("::ffff:" + PUBIP))
                            {
                                var avatarId = (string)context.Request.RouteValues["avatarId"];
                                string filePath = "parameters/" + $"avatar_{avatarId}_settings.json";
                                if (File.Exists(filePath))
                                {
                                    var settings = File.ReadAllText(filePath);
                                    await context.Response.WriteAsync(settings);
                                }
                                else
                                {
                                    await context.Response.WriteAsync("{ \"parameters\": [] }");  // Return empty JSON if no settings
                                }

                                return;
                            }

                            context.Response.StatusCode = 403; // Forbidden
                            await context.Response.WriteAsync("Access denied.");
                        });

                        // API to save AvatarID list
                        endpoints.MapPost("/api/saveAvatarListing", async context =>
                        {
                            if (context.Connection.RemoteIpAddress.ToString().Equals("::ffff:127.0.0.1") || context.Connection.RemoteIpAddress.ToString().Equals("::ffff:" + PUBIP))
                            {
                                using (StreamReader reader = new StreamReader(context.Request.Body))
                                {
                                    var content = await reader.ReadToEndAsync();
                                    Directory.CreateDirectory("parameters");
                                    File.WriteAllText("parameters/" + "AvatarListing.json", content);  // Save AvatarID list
                                    context.Response.StatusCode = 200; // OK
                                }
                                return;
                            }

                            context.Response.StatusCode = 403; // Forbidden
                            await context.Response.WriteAsync("Access denied.");
                        });

                        endpoints.MapGet("/api/loadAvatarListing", async context =>
                        {
                            if (context.Connection.RemoteIpAddress.ToString().Equals("::ffff:127.0.0.1") || context.Connection.RemoteIpAddress.ToString().Equals("::ffff:" + PUBIP))
                            {
                                string filePath = "parameters/" + "AvatarListing.json";
                                if (File.Exists(filePath))
                                {
                                    var settings = File.ReadAllText(filePath);
                                    await context.Response.WriteAsync(settings);
                                }
                                else
                                {
                                    await context.Response.WriteAsync("{\"avatarIds\": [], \"IsWhiteList\": true}");  // Default to true if no AvatarID list
                                }
                                return;
                            }

                            context.Response.StatusCode = 403; // Forbidden
                            await context.Response.WriteAsync("Access denied.");
                        });




                        endpoints.MapGet("/update/{value}", async context =>
                        {
                            var value = context.Request.RouteValues["value"]?.ToString();
                            await UpdateHtml(value);
                            await context.Response.WriteAsync("Updated");
                        });
                        endpoints.MapGet("/sendOsc", async context =>
                        {
                            var param = context.Request.Query["param"].ToString();
                            var value = context.Request.Query["value"].ToString();
                            var type = context.Request.Query["type"].ToString();
                            var oscSender = new UdpClient("127.0.0.1", oscPortOut);

                            //Console.WriteLine(param.Replace(" ", " "));

                            if (type == "Float" && float.TryParse(value, out var floatValue))
                            {
                                var message = new CoreOSC.OscMessage(new CoreOSC.Address($"{param}"), [floatValue]);
                                await oscSender.SendMessageAsync(message);
                            }
                            else if (type == "Int" && int.TryParse(value, out var intValue))
                            {
                                var message = new CoreOSC.OscMessage(new CoreOSC.Address($"{param}"), [intValue]);
                                await oscSender.SendMessageAsync(message);
                            }
                            else if (type == "Bool")
                            {
                                var message = new OscMessage();
                                if (value.ToLower().Contains("t"))
                                {
                                    message = new OscMessage(
                                    address: new Address($"{param}"),
                                    arguments: new object[] { CoreOSC.OscTrue.True });
                                }
                                else
                                {
                                    message = new OscMessage(
                                    address: new Address($"{param}"),
                                    arguments: new object[] { CoreOSC.OscFalse.False });
                                }
                                await oscSender.SendMessageAsync(message);
                            }
                            await context.Response.WriteAsync("OSC Message Sent");
                        });
                    });
                })
                .UseUrls($"http://*:{webPort}")
                .Build();
            webHost.Run();
        }

        private static async Task UpdateHtml(string oscValue)
        {
            var oscFolderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "Low", "VRChat", "VRChat", "OSC");

            // Recursively search for the JSON file
            var jsonFilePath = Directory.GetFiles(oscFolderPath, $"{oscValue}.json", SearchOption.AllDirectories).FirstOrDefault();

            if (jsonFilePath == null)
            {
                Console.WriteLine("JSON file not found.");
            }
            else
            {

                var jsonContent = File.ReadAllText(jsonFilePath);
                var json = JObject.Parse(jsonContent);

                var parameters = json["parameters"];
                var htmlContent = new StringBuilder();

                htmlContent.AppendLine("<html><head><meta charset=\"UTF-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
                htmlContent.AppendLine("<style>");
                htmlContent.AppendLine("body { background-color: #121212; color: #e0e0e0; font-family: Arial, sans-serif; padding: 10px; }");
                htmlContent.AppendLine("h1 { text-align: center; font-size: 2em; }");
                htmlContent.AppendLine(".toggle { position: relative; display: inline-block; width: 60px; height: 34px; margin-bottom: 10px; }");
                htmlContent.AppendLine(".toggle input { opacity: 0; width: 0; height: 0; }");
                htmlContent.AppendLine(".slider { position: absolute; cursor: pointer; top: 0; left: 0; right: 0; bottom: 0; background-color: #333; transition: .4s; border-radius: 34px; }");
                htmlContent.AppendLine(".slider:before { position: absolute; content: ''; height: 26px; width: 26px; border-radius: 50%; background-color: #e0e0e0; transition: .4s; }");
                htmlContent.AppendLine("input:checked + .slider { background-color: #2196F3; }");
                htmlContent.AppendLine("input:checked + .slider:before { transform: translateX(26px); }");
                htmlContent.AppendLine(".range-slider { width: 100%; background-color: #333; color: #e0e0e0; margin-bottom: 10px; }");
                htmlContent.AppendLine(".value-display { font-weight: bold; margin-left: 10px; }");
                htmlContent.AppendLine("button { padding: 10px 15px; font-size: 1.2em; margin: 5px 0; background-color: #2196F3; color: white; border: none; cursor: pointer; width: 100%; }");
                htmlContent.AppendLine("button:disabled { background-color: #555; }");
                htmlContent.AppendLine(".container { display: flex; flex-wrap: wrap; gap: 10px; justify-content: space-between; }");
                htmlContent.AppendLine(".button-container { display: flex; flex-direction: column; align-items: center; width: 100%; max-width: 200px; margin: 10px auto; }");
                htmlContent.AppendLine(".parameter-container { border: 1px solid #e0e0e0; padding: 10px; margin-bottom: 15px; border-radius: 8px; }");
                htmlContent.AppendLine(".param-header { text-align: center; font-weight: bold; font-size: 1.2em; margin-bottom: 10px; }");
                htmlContent.AppendLine(".increment-buttons { text-align: center; margin-top: 10px; }");
                htmlContent.AppendLine(".button-row { display: flex; justify-content: center; gap: 5px; margin-bottom: 5px; }");
                htmlContent.AppendLine(".bool-button { background-color: #333; color: #e0e0e0; border: none; padding: 15px; cursor: pointer; width: 100%; text-align: center; border-radius: 8px; font-size: 1.2em; }");
                htmlContent.AppendLine(".bool-button.on { background-color: #2196F3; color: white; }");
                htmlContent.AppendLine(".bool-button.off { background-color: #555; color: #e0e0e0; }");
                htmlContent.AppendLine("footer {");
                htmlContent.AppendLine("  display: flex;");
                htmlContent.AppendLine("  justify-content: center;");
                htmlContent.AppendLine("  position: fixed;");
                htmlContent.AppendLine("  bottom: -10px;");
                htmlContent.AppendLine("  left: 0px;");
                htmlContent.AppendLine("  right: 0px;");
                htmlContent.AppendLine("  margin-bottom: 0px;");
                htmlContent.AppendLine("  padding: 0px;");
                htmlContent.AppendLine("  color: #888888;");
                htmlContent.AppendLine("}");
                htmlContent.AppendLine("</style></head><body>");

                htmlContent.AppendLine("<h1>OSC Remote Control Panel</h1>");
                htmlContent.AppendLine($"  <button onclick=\"location.href=\'http://{PUBIP}/settings\'\">Setting page</button>");

                // Top Bar HTML (For Save/Load buttons)
                htmlContent.AppendLine("<div class=\"container\">");
                for (int i = 0; i < 5; i++)
                {
                    htmlContent.AppendLine($"<div class=\"button-container\">");
                    htmlContent.AppendLine($"  <button onclick=\"saveState({i})\">Save Slot #{i}</button>");
                    htmlContent.AppendLine($"  <button onclick=\"loadState({i})\">Load Slot #{i}</button>");
                    htmlContent.AppendLine("</div>");
                }
                htmlContent.AppendLine("</div>");

                // Add Controls for Toggles, Sliders and Int Adjustments
                foreach (var param in parameters)
                {
                    var name = param["name"].ToString();
                    var disabled = param["input"] == null ? "disabled" : "";
                    if (param["input"] == null) continue;
                    if (param["output"] == null) continue;
                    var reff = param["output"]["address"].ToString();
                    var id = reff.Replace("/avatar/parameters/", "");
                    var type = param["output"]["type"].ToString();

                    string globalFilePath = "parameters/" + "avatar_Global_settings.json";
                    string avatarFilePath = "parameters/" + "avatar_" + CurrentID + "_settings.json";

                    var hidden = "";

                    if (File.Exists(globalFilePath))
                    {
                        ParameterSettings globalSettings = LoadSettings(globalFilePath);

                        // First check global settings
                        if (IsBlocked(globalSettings, id, type))
                        {
                            hidden = "hidden";
                            Console.WriteLine("Blocked by global settings");
                            //continue; // Continue in your original code would skip to the next iteration
                        }

                    }

                    if (File.Exists(avatarFilePath))
                    {
                        // Load per-avatar settings
                        ParameterSettings avatarSettings = LoadSettings(avatarFilePath);

                        // Then check per-avatar settings
                        if (IsBlocked(avatarSettings, id, type))
                        {
                            hidden = "hidden";
                            Console.WriteLine("Blocked by per-avatar settings");
                            //continue;
                        }
                    }

                    // Bool (Toggle)
                    if (type == "Bool")
                    {
                        htmlContent.AppendLine($"<div {hidden} class=\"parameter-container\"><button id=\"{id}\" class=\"bool-button {(disabled == "" ? "off" : "on")}\" onclick=\"toggleBool('{reff}', this)\" {disabled}>{name}</button></div>");
                    }
                    // Float (Slider)
                    // Float (Slider + Buttons)
                    else if (type == "Float")
                    {
                        htmlContent.AppendLine($"<div {hidden} class=\"parameter-container\">");
                        htmlContent.AppendLine($"  <div class=\"param-header\">{name} <span id=\"value-{id}\" class=\"value-display\">0.000</span></div>");
                        htmlContent.AppendLine($"  <input id=\"slider-{id}\" class=\"range-slider\" type=\"range\" min=\"-1\" max=\"1\" step=\"0.001\" value=\"0\" customtag=\"Float\" oninput=\"updateValueDisplay('{id}', this.value); sendOsc('{reff}', this.value, '{type}')\" {disabled}/><br>");

                        // Buttons for incrementing and decrementing values
                        htmlContent.AppendLine($"  <div class=\"increment-buttons\">");
                        htmlContent.AppendLine($"    <div class=\"button-row\">");
                        htmlContent.AppendLine($"      <button onclick=\"adjustSlider('{reff}','{id}', 0.1)\"{disabled}>+0.1</button>");
                        htmlContent.AppendLine($"      <button onclick=\"adjustSlider('{reff}','{id}', 0.01)\"{disabled}>+0.01</button>");
                        htmlContent.AppendLine($"      <button onclick=\"adjustSlider('{reff}','{id}', 0.001)\"{disabled}>+0.001</button>");
                        htmlContent.AppendLine($"    </div>");
                        htmlContent.AppendLine($"    <div class=\"button-row\">");
                        htmlContent.AppendLine($"      <button onclick=\"adjustSlider('{reff}','{id}', -0.1)\"{disabled}>-0.1</button>");
                        htmlContent.AppendLine($"      <button onclick=\"adjustSlider('{reff}','{id}', -0.01)\"{disabled}>-0.01</button>");
                        htmlContent.AppendLine($"      <button onclick=\"adjustSlider('{reff}','{id}', -0.001)\"{disabled}>-0.001</button>");
                        htmlContent.AppendLine($"    </div>");
                        htmlContent.AppendLine($"  </div>");
                        htmlContent.AppendLine($"</div>");
                    }

                    // Int (Slider + Increment/Decrement Buttons)
                    else if (type == "Int")
                    {
                        htmlContent.AppendLine($"<div {hidden} class=\"parameter-container\">");
                        htmlContent.AppendLine($"  <div class=\"param-header\">{name} <span id=\"value-{id}\" class=\"value-display\">0</span></div>");
                        htmlContent.AppendLine($"  <input id=\"slider-{id}\" class=\"range-slider\" type=\"range\" min=\"0\" max=\"255\" step=\"1\" value=\"0\" customtag=\"Int\"oninput=\"updateValueDisplay('{id}', this.value); sendOsc('{reff}', this.value, '{type}')\" {disabled}/>");

                        // Buttons for incrementing and decrementing values
                        htmlContent.AppendLine($"  <divclass=\"increment-buttons\">");
                        htmlContent.AppendLine($"    <divclass=\"button-row\">");
                        htmlContent.AppendLine($"      <button onclick=\"incrementValue('{reff}','{id}', -1)\"{disabled}>-1</button>");
                        htmlContent.AppendLine($"      <button onclick=\"incrementValue('{reff}','{id}', 1)\"{disabled}>+1</button>");
                        htmlContent.AppendLine($"    </div>");
                        htmlContent.AppendLine($"  </div>");
                        htmlContent.AppendLine($"</div>");
                    }

                }

                // Add JavaScript for slider adjustments
                htmlContent.AppendLine("<script>");
                htmlContent.AppendLine("function sendOsc(param, value, type) {");
                htmlContent.AppendLine("  const encodedParam = encodeURIComponent(param);");
                htmlContent.AppendLine("  const encodedValue = encodeURIComponent(value);");
                htmlContent.AppendLine("  const encodedType = encodeURIComponent(type);");
                htmlContent.AppendLine("  fetch(`/sendOsc?param=${encodedParam}&value=${encodedValue}&type=${encodedType}`);");
                htmlContent.AppendLine("}");

                htmlContent.AppendLine("function updateValueDisplay(param, value) {");
                htmlContent.AppendLine("  document.getElementById(`value-${param}`).textContent = parseFloat(value).toFixed(3);");
                htmlContent.AppendLine("}");

                htmlContent.AppendLine("function adjustSlider(id,parm, increment) {");
                htmlContent.AppendLine("  const slider = document.getElementById(`slider-${parm}`);");
                htmlContent.AppendLine("  if (slider) {");
                htmlContent.AppendLine("    let newValue = parseFloat(slider.value) + increment;");
                htmlContent.AppendLine("    if (newValue >= slider.min && newValue <= slider.max) {");
                htmlContent.AppendLine("      sendOsc(id, newValue, 'Float');");
                htmlContent.AppendLine("      slider.value = newValue.toFixed(3);");
                htmlContent.AppendLine("      updateValueDisplay(parm, newValue);");
                htmlContent.AppendLine("    }");
                htmlContent.AppendLine("  }");
                htmlContent.AppendLine("}");

                htmlContent.AppendLine("function incrementValue(id,parm, amount) {");
                htmlContent.AppendLine("  const slider = document.getElementById(`slider-${parm}`);");
                htmlContent.AppendLine("  if (slider) {");
                htmlContent.AppendLine("    let newValue = parseInt(slider.value) + amount;");
                htmlContent.AppendLine("    if (newValue >= slider.min && newValue <= slider.max) {");
                htmlContent.AppendLine("      sendOsc(id, newValue, 'Int');");
                htmlContent.AppendLine("      slider.value = newValue;");
                htmlContent.AppendLine("      updateValueDisplay(parm, newValue);");
                htmlContent.AppendLine("    }");
                htmlContent.AppendLine("  }");
                htmlContent.AppendLine("}");

                htmlContent.AppendLine("function toggleBool(id, button) {");
                htmlContent.AppendLine("  const currentValue = button.classList.contains('on');");
                htmlContent.AppendLine("  const newValue = !currentValue;");
                htmlContent.AppendLine("  button.classList.toggle('on', newValue);");
                htmlContent.AppendLine("  button.classList.toggle('off', !newValue);");
                htmlContent.AppendLine("  sendOsc(id, newValue ? 't' : 'f', 'Bool');");
                htmlContent.AppendLine("}");
                htmlContent.AppendLine("</script>");

                // Add JavaScript for save/load system
                htmlContent.AppendLine("<script>");
                htmlContent.AppendLine("function saveState(slot) {");
                htmlContent.AppendLine("    const state = {");
                htmlContent.AppendLine("        toggles: [],");
                htmlContent.AppendLine("        sliders: []");
                htmlContent.AppendLine("    };");

                htmlContent.AppendLine("    document.querySelectorAll('.bool-button').forEach(toggle => {");
                htmlContent.AppendLine("        state.toggles.push({ id: toggle.id, checked: toggle.classList.contains('on') });");
                htmlContent.AppendLine("    });");

                htmlContent.AppendLine("    document.querySelectorAll('.range-slider').forEach(slider => {");
                htmlContent.AppendLine("        const type = slider.getAttribute('customtag');");
                htmlContent.AppendLine("        console.log(`Slider ID: ${slider.id}, Type: ${type}`);"); // Log the type
                htmlContent.AppendLine("        state.sliders.push({ id: slider.id, value: slider.value, type: type });");
                htmlContent.AppendLine("    });");

                htmlContent.AppendLine("    console.log('State before sending:', JSON.stringify(state));"); // Log the state

                htmlContent.AppendLine("    fetch(`/saveState?slot=${slot}&t=${new Date().getTime()}`, {");
                htmlContent.AppendLine("        method: 'POST',");
                htmlContent.AppendLine("        headers: {");
                htmlContent.AppendLine("            'Content-Type': 'application/json'");
                htmlContent.AppendLine("        },");
                htmlContent.AppendLine("        body: JSON.stringify(state)");
                htmlContent.AppendLine("    }).then(response => response.text()).then(result => {");
                htmlContent.AppendLine("        console.log(result);");
                htmlContent.AppendLine("    });");
                htmlContent.AppendLine("}");
                htmlContent.AppendLine("</script>");



                htmlContent.AppendLine("<script>");
                htmlContent.AppendLine("function loadState(slot) {");
                htmlContent.AppendLine("    fetch(`/loadState?slot=${slot}`)");
                htmlContent.AppendLine("    .then(response => response.json())");
                htmlContent.AppendLine("    .then(state => {");
                htmlContent.AppendLine("        console.log(state.Toggles)");
                htmlContent.AppendLine("        console.log(state.Sliders)");
                htmlContent.AppendLine("        if (state.Toggles) {");
                htmlContent.AppendLine("            state.Toggles.forEach(toggle => {");
                htmlContent.AppendLine("                try {");
                htmlContent.AppendLine("                    const element = document.getElementById(toggle.Id);");
                htmlContent.AppendLine("                    if (element) {");
                htmlContent.AppendLine("                        element.classList.toggle('on', !toggle.Checked);");
                htmlContent.AppendLine("                        element.classList.toggle('off', toggle.Checked);");
                htmlContent.AppendLine("                        toggleBool('/avatar/parameters/'+toggle.Id, element)");
                htmlContent.AppendLine("                    }");
                htmlContent.AppendLine("                } catch (error) {");
                htmlContent.AppendLine("                    console.error(`Error setting toggle state for ${toggle.Id}:`, error);");
                htmlContent.AppendLine("                }");
                htmlContent.AppendLine("            });");
                htmlContent.AppendLine("        }");
                htmlContent.AppendLine("        if (state.Sliders) {");
                htmlContent.AppendLine("            state.Sliders.forEach(slider => {");
                htmlContent.AppendLine("                try {");
                htmlContent.AppendLine("                    const element = document.getElementById(slider.Id);");
                htmlContent.AppendLine("                    if (element) {");
                htmlContent.AppendLine("                        element.value = slider.Value;");
                htmlContent.AppendLine("                        updateValueDisplay(element.id.replace('slider-', ''), slider.Value);");
                htmlContent.AppendLine("                        sendOsc('/avatar/parameters/' + element.id.replace('slider-', ''), slider.Value, slider.Type);");
                htmlContent.AppendLine("                    }");
                htmlContent.AppendLine("                } catch (error) {");
                htmlContent.AppendLine("                    console.error(`Error setting slider state for ${slider.Id}:`, error);");
                htmlContent.AppendLine("                }");
                htmlContent.AppendLine("            });");
                htmlContent.AppendLine("        }");
                htmlContent.AppendLine("    })");
                htmlContent.AppendLine("    .catch(error => {");
                htmlContent.AppendLine("        console.error('Error loading state:', error);");
                htmlContent.AppendLine("    });");
                htmlContent.AppendLine("}");
                htmlContent.AppendLine("</script>");








                // Add JavaScript for sending OSC state
                htmlContent.AppendLine("<script>");
                htmlContent.AppendLine("function sendOscState(state) {");
                htmlContent.AppendLine("    state.toggles.forEach(toggle => {");
                htmlContent.AppendLine("        fetch(`/sendOsc?param=${encodeURIComponent(toggle.id.replace('toggle-', '/avatar/parameters/'))}&value=${toggle.checked ? 'true' : 'false'}&type=Bool`);");
                htmlContent.AppendLine("    });");

                htmlContent.AppendLine("    state.sliders.forEach(slider => {");
                htmlContent.AppendLine("        fetch(`/sendOsc?param=${encodeURIComponent(slider.id.replace('slider-', '/avatar/parameters/'))}&value=${encodeURIComponent(slider.value)}&type=Float`);");
                htmlContent.AppendLine("    });");
                htmlContent.AppendLine("}");
                htmlContent.AppendLine("</script>");


                // Add WebSocket Handling
                htmlContent.AppendLine("<script>");
                htmlContent.AppendLine($"  var ws = new WebSocket(`ws://{PUBIP}:{webPort}/ws`);");
                htmlContent.AppendLine();
                htmlContent.AppendLine("  ws.onopen = function() {");
                htmlContent.AppendLine("    console.log('WebSocket connection established.');");
                htmlContent.AppendLine("  };");
                htmlContent.AppendLine();
                htmlContent.AppendLine("  ws.onclose = function() {");
                htmlContent.AppendLine("    console.log('WebSocket connection closed.');");
                htmlContent.AppendLine("  };");
                htmlContent.AppendLine();
                htmlContent.AppendLine("  ws.onmessage = function(event) {");
                htmlContent.AppendLine("    var data = JSON.parse(event.data);");
                htmlContent.AppendLine();
                htmlContent.AppendLine("    if (data.action === 'updateHtml') {");
                htmlContent.AppendLine("      // Update the entire HTML content");
                htmlContent.AppendLine("      document.open();");
                htmlContent.AppendLine("      document.write(decodeURIComponent(data.html));");
                htmlContent.AppendLine("      document.close();");
                htmlContent.AppendLine("    } else if (data.param) {");
                htmlContent.AppendLine("      var param = data.param;");
                htmlContent.AppendLine("      var value = data.value;");
                htmlContent.AppendLine("      var slider = document.getElementById(`slider-${param}`);");
                htmlContent.AppendLine("      var toggleButton = document.getElementById(`${param}`);");
                htmlContent.AppendLine();
                htmlContent.AppendLine("      // Update slider values");
                htmlContent.AppendLine("      if (slider) {");
                htmlContent.AppendLine("        slider.value = value;");
                htmlContent.AppendLine("        updateValueDisplay(param, value);");
                htmlContent.AppendLine("      }");
                htmlContent.AppendLine();
                htmlContent.AppendLine("      // Update toggle button states");
                htmlContent.AppendLine("      if (toggleButton) {");
                htmlContent.AppendLine("        var isOn = value === 't';");
                htmlContent.AppendLine("        toggleButton.classList.toggle('on', !isOn);");
                htmlContent.AppendLine("        toggleButton.classList.toggle('off', isOn);");
                htmlContent.AppendLine("        toggleBool(param, toggleButton)");
                htmlContent.AppendLine("      }");
                htmlContent.AppendLine("    }");
                htmlContent.AppendLine("  };");
                htmlContent.AppendLine("</script>");

                htmlContent.AppendLine("<footer>");
                htmlContent.AppendLine($"  <p>Current OSC avatar ID : {CurrentID}</p>");
                htmlContent.AppendLine("</footer>");

                htmlContent.AppendLine("</body></html>");
                indexHTML = htmlContent.ToString();

                // Notify WebSocket clients about the HTML update
                var broadcastMessage = $"{{\"action\":\"updateHtml\",\"html\":\"{Uri.EscapeDataString(htmlContent.ToString())}\"}}";
                await WebSocketHandler.BroadcastMessage(broadcastMessage);
            }
        }
        private static void StartOscListener()
        {
            Console.WriteLine("OSC Server Started");

            var udpClient = new UdpClient(oscPortIn);

            // Receive OSC messages asynchronously
            Task.Run(async () =>
            {
                while (true)
                {
                    var packet = await udpClient.ReceiveMessageAsync();
                    var messageReceived = packet;
                    Console.WriteLine($"Received OSC message: {messageReceived.Address.Value} {string.Join(", ", messageReceived.Arguments)}");

                    if (messageReceived.Address.Value == "/avatar/change")
                    {
                        var value = messageReceived.Arguments.ElementAt(0).ToString();
                        string filePath = "parameters/" + "AvatarListing.json";
                        if (!File.Exists(filePath) || CheckAvatarId(File.ReadAllText(filePath), value))
                        {
                            CurrentID = value;
                            await UpdateHtml(value);
                        }
                        else
                        {
                            Console.WriteLine("Blocked by avatar settings");
                        }
                    }
                    else
                    {
                        var param = messageReceived.Address.Value.Replace("/avatar/parameters/", "");
                        var value = messageReceived.Arguments.FirstOrDefault()?.ToString() ?? "null";
                        var nv = value.ToString().Replace("CoreOSC.OscFalse", "f");
                        nv = nv.ToString().Replace("CoreOSC.OscTrue", "t");
                        var broadcastMessage = $"{{\"param\":\"{param}\",\"value\":\"{nv}\"}}";
                        await WebSocketHandler.BroadcastMessage(broadcastMessage);
                    }
                }
            });
        }
        public static bool CheckAvatarId(string json, string input)
        {
            AvatarList avatarList = JsonConvert.DeserializeObject<AvatarList>(json);

            // Check if the input is in the AvatarIds array
            bool isInList = Array.Exists(avatarList.AvatarIds, id => id.Equals(input, StringComparison.OrdinalIgnoreCase));

            // Return the result based on IsWhiteList
            return avatarList.IsWhiteList ? isInList : !isInList;
        }

        public static bool WildcardMatch(string input, string pattern)
        {
            // Convert wildcard pattern to regex pattern
            string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return Regex.IsMatch(input, regexPattern);
        }
        // Function to get the parameter names by type (Bool, Int, Float)
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
        // Function to load the settings from a JSON file
        public static ParameterSettings LoadSettings(string filePath)
        {
            if (File.Exists(filePath))
            {
                return JsonConvert.DeserializeObject<ParameterSettings>(File.ReadAllText(filePath));
            }
            return new ParameterSettings(); // Return an empty settings object if file doesn't exist
        }
        // Function to check if a parameter is blocked (globally or per-avatar)
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


    }


    public class WebSocketHandler
    {
        private static readonly List<WebSocket> _webSockets = new List<WebSocket>();

        private readonly WebSocket _webSocket;

        public WebSocketHandler(WebSocket webSocket)
        {
            _webSocket = webSocket;
            lock (_webSockets)
            {
                _webSockets.Add(webSocket);
            }
        }

        public async Task HandleAsync()
        {
            var buffer = new byte[1024 * 4];
            WebSocketReceiveResult result;

            do
            {
                result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            } while (!result.CloseStatus.HasValue);

            lock (_webSockets)
            {
                _webSockets.Remove(_webSocket);
            }

            await _webSocket.CloseAsync(result.CloseStatus.Value, result.CloseStatusDescription, CancellationToken.None);
        }

        public static async Task BroadcastMessage(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            var segment = new ArraySegment<byte>(buffer);

            var disconnectedSockets = new List<WebSocket>();
            //Console.WriteLine("llll " + _webSockets.Count);
            foreach (var webSocket in _webSockets)
            {
                // Console.WriteLine(webSocket.State);

                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                else
                {
                    disconnectedSockets.Add(webSocket);
                }
            }

            foreach (var socket in disconnectedSockets)
            {
                _webSockets.Remove(socket);
            }
        }
    }
}
public class State
{
    public List<Toggle> Toggles { get; set; }
    public List<Slider> Sliders { get; set; }
}

public class Toggle
{
    public string Id { get; set; }
    public bool Checked { get; set; }
}

public class Slider
{
    public string Id { get; set; }
    public float Value { get; set; }  // Use 'float' or 'int' depending on your needs
    public string Type { get; set; }  // Add this if you're passing the type (Float/Int)
}

public class AvatarList
{
    public string[] AvatarIds { get; set; }
    public bool IsWhiteList { get; set; }
}

public class Parameter
{
    public string Name { get; set; }
    public string Type { get; set; }
}

public class ParameterSettings
{
    public List<Parameter> Parameters { get; set; }
    public string BoolMode { get; set; }
    public string IntMode { get; set; }
    public string FloatMode { get; set; }
}
public class UdpRoute
{
    public string InputPort { get; set; }  // Change to string to handle both numbers and placeholders
    public List<UdpOutput> Outputs { get; set; }
}

public class UdpOutput
{
    public string Ip { get; set; }
    public string Port { get; set; }  // Keep as string to handle placeholders
}
public class RoutingConfiguration
{
    public int InputPort { get; set; }
    public List<Output> Outputs { get; set; }
}

public class Output
{
    public string Ip { get; set; }
    public int Port { get; set; }
}

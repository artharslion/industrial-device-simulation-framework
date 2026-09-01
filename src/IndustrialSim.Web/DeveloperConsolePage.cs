namespace IndustrialSim.Web;

public static class DeveloperConsolePage
{
    public static IEndpointRouteBuilder MapIndustrialSimDeveloperConsole(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", () => Results.Content(Html, "text/html; charset=utf-8"));
        return endpoints;
    }

    private const string Html = """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Industrial Device Simulation</title>
  <style>
    :root { --ink:#090d0d; --panel:#111817; --panel2:#17211f; --line:#30413d; --muted:#8ba09a; --text:#e8f0ed; --amber:#ffb547; --cyan:#58e1d2; --red:#ff6b62; --green:#7fda8b; --shadow:0 18px 60px rgba(0,0,0,.38); }
    * { box-sizing:border-box; }
    body { margin:0; color:var(--text); background:var(--ink); font-family:"Bahnschrift","DIN Alternate","Trebuchet MS",sans-serif; min-height:100vh; }
    body::before { content:""; position:fixed; inset:0; pointer-events:none; opacity:.17; background-image:linear-gradient(rgba(88,225,210,.18) 1px,transparent 1px),linear-gradient(90deg,rgba(88,225,210,.12) 1px,transparent 1px); background-size:42px 42px; mask-image:linear-gradient(to bottom,black,transparent 85%); }
    .shell { width:min(1500px,calc(100% - 32px)); margin:0 auto; padding:28px 0 48px; position:relative; }
    header { display:grid; grid-template-columns:1fr auto; align-items:end; gap:24px; padding:4px 0 22px; border-bottom:1px solid var(--line); }
    .kicker { color:var(--amber); letter-spacing:.22em; text-transform:uppercase; font:600 12px "Cascadia Mono",monospace; }
    h1 { margin:7px 0 0; font-size:clamp(28px,4vw,56px); line-height:.95; letter-spacing:-.045em; font-weight:750; }
    .identity { text-align:right; color:var(--muted); font:13px "Cascadia Mono",monospace; }
    .identity strong { display:block; color:var(--text); font-size:16px; margin-bottom:4px; }
    .status-rail { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:10px; margin:18px 0; }
    .status { background:linear-gradient(145deg,var(--panel2),var(--panel)); border:1px solid var(--line); padding:13px 15px; box-shadow:var(--shadow); }
    .status span { color:var(--muted); text-transform:uppercase; letter-spacing:.13em; font:10px "Cascadia Mono",monospace; }
    .status strong { display:block; margin-top:7px; font:700 17px "Cascadia Mono",monospace; }
    .lamp { display:inline-block; width:8px; height:8px; border-radius:50%; margin-right:8px; background:var(--muted); box-shadow:0 0 0 3px rgba(139,160,154,.1); }
    .lamp.running,.lamp.active { background:var(--green); box-shadow:0 0 15px rgba(127,218,139,.8); }
    .lamp.paused { background:var(--amber); box-shadow:0 0 15px rgba(255,181,71,.8); }
    .lamp.stopped,.lamp.error { background:var(--red); box-shadow:0 0 15px rgba(255,107,98,.65); }
    main { display:grid; grid-template-columns:minmax(0,1.4fr) minmax(330px,.8fr); gap:16px; align-items:start; }
    .stack { display:grid; gap:16px; }
    .panel { background:rgba(17,24,23,.94); border:1px solid var(--line); box-shadow:var(--shadow); overflow:hidden; }
    .panel-head { display:flex; justify-content:space-between; align-items:center; padding:13px 16px; border-bottom:1px solid var(--line); background:rgba(23,33,31,.88); }
    .panel-head h2 { margin:0; text-transform:uppercase; letter-spacing:.14em; font-size:12px; }
    .panel-head small { color:var(--muted); font:11px "Cascadia Mono",monospace; }
    .panel-body { padding:16px; }
    table { width:100%; border-collapse:collapse; font:13px "Cascadia Mono",monospace; }
    th { color:var(--muted); font-weight:500; text-transform:uppercase; letter-spacing:.1em; font-size:10px; text-align:left; padding:0 10px 10px; }
    td { padding:11px 10px; border-top:1px solid rgba(48,65,61,.65); }
    td:last-child { text-align:right; color:var(--cyan); }
    .controls { display:flex; flex-wrap:wrap; gap:8px; }
    button { appearance:none; border:1px solid var(--line); background:#1b2724; color:var(--text); padding:10px 13px; font:700 11px "Cascadia Mono",monospace; text-transform:uppercase; letter-spacing:.08em; cursor:pointer; transition:.16s ease; }
    button:hover { border-color:var(--cyan); color:var(--cyan); transform:translateY(-1px); }
    button.primary { background:var(--amber); color:#17110a; border-color:var(--amber); }
    button.danger { border-color:#713b38; color:#ff9c96; }
    textarea,input,select { width:100%; border:1px solid var(--line); background:#0b1110; color:var(--text); padding:10px 11px; font:12px/1.55 "Cascadia Mono",monospace; outline:none; }
    textarea:focus,input:focus,select:focus { border-color:var(--cyan); box-shadow:0 0 0 2px rgba(88,225,210,.1); }
    textarea { min-height:210px; resize:vertical; }
    label { display:block; color:var(--muted); text-transform:uppercase; letter-spacing:.1em; font:10px "Cascadia Mono",monospace; margin:0 0 6px; }
    .form-grid { display:grid; grid-template-columns:1fr 1fr; gap:10px; }
    .form-grid .wide { grid-column:1/-1; }
    .terminal { min-height:270px; max-height:420px; overflow:auto; background:#070a0a; padding:13px; font:11px/1.6 "Cascadia Mono",monospace; color:#b9c8c4; white-space:pre-wrap; }
    .fault-list { display:grid; gap:8px; margin-top:12px; }
    .fault-row { display:grid; grid-template-columns:1fr auto; gap:8px; align-items:center; border-left:3px solid var(--red); padding:8px 10px; background:#161c1b; font:11px "Cascadia Mono",monospace; }
    #validation-error { display:none; position:sticky; top:10px; z-index:4; margin:0 0 12px; padding:11px 14px; border:1px solid #8a3b37; background:#341d1b; color:#ffd1ce; font:12px "Cascadia Mono",monospace; }
    #validation-error.visible { display:block; }
    .hint { color:var(--muted); font-size:12px; line-height:1.5; }
    @media(max-width:900px){ main{grid-template-columns:1fr}.status-rail{grid-template-columns:1fr 1fr}header{grid-template-columns:1fr}.identity{text-align:left}.form-grid{grid-template-columns:1fr}.form-grid .wide{grid-column:auto} }
    @media(prefers-reduced-motion:reduce){ *{transition:none!important} }
  </style>
</head>
<body>
  <div class="shell">
    <div id="validation-error" role="alert"></div>
    <header>
      <div><div class="kicker">Runtime Operations / v0.1</div><h1>Industrial Device<br>Simulation</h1></div>
      <div class="identity"><strong id="device-id">Connecting…</strong><span id="device-type">runtime discovery</span></div>
    </header>
    <section class="status-rail">
      <div class="status"><span>Runtime</span><strong><i id="runtime-lamp" class="lamp"></i><b id="runtime-state">Loading</b></strong></div>
      <div class="status"><span>OPC UA</span><strong><i id="opcua-lamp" class="lamp"></i><b id="opcua-state">Unknown</b></strong></div>
      <div class="status"><span>Modbus TCP</span><strong><i id="modbus-lamp" class="lamp"></i><b id="modbus-state">Unknown</b></strong></div>
      <div class="status"><span>Scenario / Fault</span><strong><i id="activity-lamp" class="lamp"></i><b id="activity-state">Idle</b></strong></div>
    </section>
    <main>
      <div class="stack">
        <section class="panel"><div class="panel-head"><h2>StateStore datapoints</h2><small id="sim-time">00:00:00</small></div><div class="panel-body"><table><thead><tr><th>Datapoint</th><th>Runtime value</th></tr></thead><tbody id="state-body"></tbody></table></div></section>
        <section class="panel"><div class="panel-head"><h2>Runtime control</h2><small>Start · Paused · Stopped · Reset</small></div><div class="panel-body"><div class="controls"><button class="primary" data-runtime="start">Start / Resume</button><button data-runtime="pause">Pause</button><button class="danger" data-runtime="stop">Stop</button><button data-runtime="reset">Reset</button><button id="tick">Advance 1s</button></div></div></section>
        <section class="panel"><div class="panel-head"><h2>Runtime events</h2><small>ordered observer stream</small></div><div id="event-terminal" class="terminal">Waiting for events…</div></section>
      </div>
      <div class="stack">
        <section class="panel"><div class="panel-head"><h2>Scenario control</h2><small id="scenario-state">Stopped</small></div><div class="panel-body">
          <label for="scenario-yaml">Scenario YAML</label><textarea id="scenario-yaml">scenario:
  name: operator-sequence
  steps:
    - at: 0s
      set:
        device: pump-001
        datapoint: speed
        value: 900</textarea>
          <div class="controls" style="margin-top:10px"><button id="run-scenario" class="primary">Run Scenario</button><button id="stop-scenario" class="danger">Stop Scenario</button></div>
        </div></section>
        <section class="panel"><div class="panel-head"><h2>Fault control</h2><small>Scheduled · Active · Recovered</small></div><div class="panel-body">
          <div class="form-grid"><div><label for="fault-category">Category</label><select id="fault-category"><option>Data</option><option>Device</option><option>Network</option></select></div><div><label for="fault-type">Type</label><input id="fault-type" value="spike"></div><div><label for="fault-target">Target</label><input id="fault-target" value="speed"></div><div><label for="fault-parameter">Parameter</label><input id="fault-parameter" value="25"></div><div class="wide"><button id="activate-fault" class="danger">Activate Fault</button></div></div>
          <div id="fault-list" class="fault-list"><div class="hint">No active faults.</div></div>
        </div></section>
      </div>
    </main>
  </div>
  <script>
    const $ = id => document.getElementById(id);
    const request = async (url, options={}) => { const response = await fetch(url, options); const text = await response.text(); let data={}; try{data=text?JSON.parse(text):{}}catch{data={error:text}} if(!response.ok) throw new Error(data.error||`${response.status} ${response.statusText}`); return data; };
    const showError = error => { const box=$('validation-error'); box.textContent=error.message||String(error); box.classList.add('visible'); setTimeout(()=>box.classList.remove('visible'),6000); };
    const lamp = (id,state) => { const el=$(id); el.className=`lamp ${String(state).toLowerCase()}`; };
    const refresh = async () => { try {
      const [state,runtime,protocols,events,faults] = await Promise.all(['/api/state','/api/runtime','/api/protocols','/api/events','/api/faults'].map(url=>request(url)));
      $('device-id').textContent=runtime.deviceId; $('device-type').textContent=`${runtime.deviceType} · ${runtime.deterministic?'deterministic':'real-time'} · seed ${runtime.seed}`;
      $('runtime-state').textContent=runtime.state; lamp('runtime-lamp',runtime.state); $('sim-time').textContent=runtime.time;
      $('opcua-state').textContent=protocols.opcua?'Online':'Offline'; lamp('opcua-lamp',protocols.opcua?'running':'stopped');
      $('modbus-state').textContent=protocols.modbus?'Online':'Offline'; lamp('modbus-lamp',protocols.modbus?'running':'stopped');
      const active=runtime.scenario.running||runtime.activeFaults>0; $('activity-state').textContent=runtime.scenario.running?`Scenario: ${runtime.scenario.name}`:runtime.activeFaults?`${runtime.activeFaults} Fault Active`:'Idle'; lamp('activity-lamp',active?'active':'stopped'); $('scenario-state').textContent=runtime.scenario.running?'Running':'Stopped';
      $('state-body').innerHTML=Object.entries(state).map(([name,value])=>`<tr><td>${name}</td><td>${JSON.stringify(value)}</td></tr>`).join('')||'<tr><td colspan="2">No datapoints</td></tr>';
      $('event-terminal').textContent=events.length?events.slice(-80).map((event,index)=>`${String(index+1).padStart(3,'0')}  ${JSON.stringify(event)}`).join('\n'):'Waiting for events…';
      const faultCategories=['Data','Device','Network']; $('fault-list').innerHTML=faults.length?faults.map(f=>`<div class="fault-row"><span>${f.id} · ${faultCategories[f.category]??f.category} · ${f.type}</span><button data-recover="${f.id}">Recover</button></div>`).join(''):'<div class="hint">No active faults.</div>';
      document.querySelectorAll('[data-recover]').forEach(button=>button.onclick=async()=>{try{await request(`/api/fault/recover/${button.dataset.recover}`,{method:'POST'});await refresh()}catch(error){showError(error)}});
      if($('scenario-yaml').value.includes('pump-001')) $('scenario-yaml').value=$('scenario-yaml').value.replaceAll('pump-001',runtime.deviceId);
      $('tick').disabled=!runtime.deterministic;
    } catch(error){ showError(error); } };
    document.querySelectorAll('[data-runtime]').forEach(button=>button.onclick=async()=>{try{await request(`/api/runtime/${button.dataset.runtime}`,{method:'POST'});await refresh()}catch(error){showError(error)}});
    $('tick').onclick=async()=>{try{await request('/api/runtime/tick/1',{method:'POST'});await refresh()}catch(error){showError(error)}};
    $('run-scenario').onclick=async()=>{try{await request('/api/scenario',{method:'POST',headers:{'Content-Type':'text/yaml'},body:$('scenario-yaml').value});await refresh()}catch(error){showError(error)}};
    $('stop-scenario').onclick=async()=>{try{await request('/api/scenario',{method:'DELETE'});await refresh()}catch(error){showError(error)}};
    $('activate-fault').onclick=async()=>{try{const parameter=$('fault-parameter').value;await request('/api/fault',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({id:`ui-${Date.now()}`,category:$('fault-category').value,target:$('fault-target').value,type:$('fault-type').value,metadata:parameter?{parameter}:null})});await refresh()}catch(error){showError(error)}};
    refresh(); setInterval(refresh,1000);
  </script>
</body>
</html>
""";
}

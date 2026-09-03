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
  <meta name="color-scheme" content="dark">
  <title>Industrial Device Simulation</title>
  <style>
    :root {
      --canvas:#08090a; --sidebar:#0c0c0e; --surface:#101012; --surface-raised:#141416; --surface-hover:#19191c;
      --line:#242428; --line-strong:#313137; --text:#f1f1f3; --text-secondary:#a2a2aa; --text-tertiary:#686870;
      --accent:#8b7cf6; --accent-soft:rgba(139,124,246,.13); --green:#5bc98c; --amber:#e6b566; --red:#ed6a78;
      --red-soft:rgba(237,106,120,.12); --radius:8px; --shadow:0 18px 60px rgba(0,0,0,.28);
    }
    * { box-sizing:border-box; }
    html { scroll-behavior:smooth; }
    body { margin:0; min-height:100vh; color:var(--text); background:var(--canvas); font-family:"IBM Plex Sans","Aptos","Segoe UI",sans-serif; font-size:14px; -webkit-font-smoothing:antialiased; }
    button,input,select,textarea { font:inherit; }
    button:focus-visible,input:focus-visible,select:focus-visible,textarea:focus-visible,a:focus-visible { outline:2px solid var(--accent); outline-offset:2px; }
    .app-shell { display:grid; grid-template-columns:232px minmax(0,1fr); min-height:100vh; }
    .workspace-sidebar { position:sticky; top:0; z-index:5; display:flex; flex-direction:column; height:100vh; padding:14px 12px; background:var(--sidebar); border-right:1px solid var(--line); }
    .brand { display:flex; align-items:center; gap:10px; padding:7px 8px 18px; }
    .brand-mark { display:grid; place-items:center; width:28px; height:28px; border:1px solid #6f62d9; border-radius:7px; background:linear-gradient(145deg,#9d91ff,#6656d9); color:white; font:700 13px "Cascadia Mono",monospace; box-shadow:0 0 0 4px rgba(139,124,246,.08); }
    .brand-copy strong { display:block; font-size:13px; letter-spacing:-.01em; }
    .brand-copy span { display:block; margin-top:2px; color:var(--text-tertiary); font-size:11px; }
    .sidebar-label { padding:13px 9px 7px; color:var(--text-tertiary); font-size:10px; font-weight:650; letter-spacing:.09em; text-transform:uppercase; }
    .sidebar-nav { display:grid; gap:2px; }
    .nav-item { display:flex; align-items:center; gap:10px; min-height:34px; padding:0 9px; border:1px solid transparent; border-radius:6px; color:var(--text-secondary); text-decoration:none; font-size:13px; transition:background .15s ease,color .15s ease,border-color .15s ease; }
    .nav-item::before { content:""; width:6px; height:6px; border:1px solid currentColor; border-radius:2px; opacity:.72; }
    .nav-item:hover { color:var(--text); background:var(--surface-hover); }
    .nav-item.active { color:var(--text); background:var(--accent-soft); border-color:rgba(139,124,246,.18); }
    .nav-item.active::before { background:var(--accent); border-color:var(--accent); box-shadow:0 0 8px rgba(139,124,246,.55); }
    .sidebar-spacer { flex:1; }
    .environment-card { margin:8px 2px 2px; padding:11px; border:1px solid var(--line); border-radius:var(--radius); background:var(--surface); }
    .environment-card span { display:block; color:var(--text-tertiary); font-size:10px; text-transform:uppercase; letter-spacing:.08em; }
    .environment-card strong { display:block; overflow:hidden; margin-top:6px; font-size:12px; font-weight:550; text-overflow:ellipsis; white-space:nowrap; }
    .environment-card small { display:block; margin-top:4px; color:var(--text-secondary); font:10px "Cascadia Mono",monospace; }
    .command-center { min-width:0; }
    .workspace-header { position:sticky; top:0; z-index:4; display:flex; align-items:center; justify-content:space-between; min-height:57px; padding:0 28px; border-bottom:1px solid var(--line); background:rgba(8,9,10,.86); backdrop-filter:blur(18px); }
    .breadcrumb { display:flex; align-items:center; gap:8px; color:var(--text-tertiary); font-size:12px; }
    .breadcrumb strong { color:var(--text-secondary); font-weight:550; }
    .breadcrumb .slash { color:#3d3d43; }
    .header-meta { display:flex; align-items:center; gap:9px; color:var(--text-tertiary); font:10px "Cascadia Mono",monospace; }
    .sync-dot { width:6px; height:6px; border-radius:50%; background:var(--green); box-shadow:0 0 8px rgba(91,201,140,.7); }
    .content { width:min(1480px,100%); margin:0 auto; padding:28px; }
    .page-intro { display:flex; align-items:flex-start; justify-content:space-between; gap:24px; margin-bottom:24px; }
    .eyebrow { margin-bottom:7px; color:var(--accent); font-size:10px; font-weight:700; letter-spacing:.12em; text-transform:uppercase; }
    h1 { margin:0; font-size:25px; line-height:1.2; letter-spacing:-.035em; font-weight:630; }
    .page-intro p { max-width:620px; margin:8px 0 0; color:var(--text-secondary); font-size:13px; line-height:1.55; }
    .device-chip { display:flex; align-items:center; gap:9px; min-width:190px; padding:9px 11px; border:1px solid var(--line); border-radius:var(--radius); background:var(--surface); }
    .device-glyph { display:grid; place-items:center; width:28px; height:28px; border-radius:6px; background:var(--accent-soft); color:#b8afff; font:700 11px "Cascadia Mono",monospace; }
    .device-chip strong { display:block; font-size:12px; font-weight:600; }
    .device-chip span { display:block; margin-top:2px; color:var(--text-tertiary); font-size:10px; }
    .status-grid { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:8px; margin-bottom:16px; }
    .status-card { min-width:0; padding:13px 14px; border:1px solid var(--line); border-radius:var(--radius); background:var(--surface); }
    .status-card-label { display:flex; align-items:center; justify-content:space-between; gap:8px; color:var(--text-tertiary); font-size:10px; font-weight:650; letter-spacing:.07em; text-transform:uppercase; }
    .status-card strong { display:block; overflow:hidden; margin-top:8px; color:var(--text); font-size:13px; font-weight:560; text-overflow:ellipsis; white-space:nowrap; }
    .lamp { display:inline-block; width:7px; height:7px; border-radius:50%; background:var(--text-tertiary); }
    .lamp.running,.lamp.active { background:var(--green); box-shadow:0 0 8px rgba(91,201,140,.58); }
    .lamp.paused { background:var(--amber); box-shadow:0 0 8px rgba(230,181,102,.5); }
    .lamp.stopped,.lamp.error { background:var(--red); box-shadow:0 0 8px rgba(237,106,120,.45); }
    .lamp.idle { background:var(--text-tertiary); box-shadow:none; }
    .dashboard-grid { display:grid; grid-template-columns:repeat(12,minmax(0,1fr)); gap:12px; align-items:start; }
    .panel { min-width:0; overflow:hidden; border:1px solid var(--line); border-radius:var(--radius); background:var(--surface); box-shadow:var(--shadow); }
    .state-panel { grid-column:span 7; } .runtime-panel { grid-column:span 5; } .events-panel { grid-column:span 7; } .scenario-panel { grid-column:span 5; } .fault-panel { grid-column:8 / span 5; }
    .panel-head { display:flex; align-items:center; justify-content:space-between; gap:16px; min-height:46px; padding:0 15px; border-bottom:1px solid var(--line); }
    .panel-title { display:flex; align-items:center; gap:9px; min-width:0; }
    .panel-icon { width:7px; height:7px; border:1px solid var(--text-tertiary); border-radius:2px; transform:rotate(45deg); }
    .panel-head h2 { margin:0; font-size:12px; font-weight:600; letter-spacing:-.01em; }
    .panel-head small { overflow:hidden; color:var(--text-tertiary); font:10px "Cascadia Mono",monospace; text-overflow:ellipsis; white-space:nowrap; }
    .panel-body { padding:15px; }
    table { width:100%; border-collapse:collapse; table-layout:fixed; }
    th { padding:0 11px 9px; color:var(--text-tertiary); font-size:10px; font-weight:650; letter-spacing:.07em; text-align:left; text-transform:uppercase; }
    th:last-child,td:last-child { text-align:right; }
    td { height:42px; padding:0 11px; border-top:1px solid var(--line); color:var(--text-secondary); font-size:12px; }
    td:first-child { color:var(--text); font-weight:520; }
    td code { color:#c8c3ff; font:11px "Cascadia Mono",monospace; }
    .type-badge { display:inline-flex; padding:2px 6px; border:1px solid var(--line); border-radius:4px; color:var(--text-tertiary); font:9px "Cascadia Mono",monospace; text-transform:uppercase; }
    .controls { display:flex; flex-wrap:wrap; gap:7px; }
    button { appearance:none; min-height:32px; padding:0 11px; border:1px solid var(--line-strong); border-radius:6px; background:var(--surface-raised); color:var(--text-secondary); font-size:11px; font-weight:600; cursor:pointer; transition:background .14s ease,border-color .14s ease,color .14s ease,transform .14s ease; }
    button:hover:not(:disabled) { border-color:#46464d; background:var(--surface-hover); color:var(--text); transform:translateY(-1px); }
    button.primary { border-color:#7768e8; background:#7365df; color:white; box-shadow:0 3px 12px rgba(83,68,196,.25); }
    button.primary:hover:not(:disabled) { border-color:#9588ff; background:#8072eb; }
    button.danger { border-color:rgba(237,106,120,.27); background:var(--red-soft); color:#f18d98; }
    button:disabled { opacity:.38; cursor:not-allowed; }
    .runtime-copy { margin:0 0 13px; color:var(--text-secondary); font-size:12px; line-height:1.55; }
    textarea,input,select { width:100%; border:1px solid var(--line-strong); border-radius:6px; background:#0b0b0d; color:var(--text); outline:none; transition:border-color .14s ease,box-shadow .14s ease; }
    textarea:focus,input:focus,select:focus { border-color:#6558c7; box-shadow:0 0 0 3px rgba(139,124,246,.09); }
    input,select { height:34px; padding:0 10px; font-size:12px; }
    textarea { min-height:210px; padding:11px; resize:vertical; font:11px/1.65 "Cascadia Mono",monospace; tab-size:2; }
    label { display:block; margin:0 0 6px; color:var(--text-secondary); font-size:10px; font-weight:600; }
    .form-grid { display:grid; grid-template-columns:1fr 1fr; gap:11px; } .form-grid .wide { grid-column:1/-1; } .action-row { margin-top:10px; }
    .event-terminal { min-height:290px; max-height:410px; overflow:auto; background:#0b0b0d; }
    .event-row { display:grid; grid-template-columns:70px minmax(105px,.55fr) minmax(0,1.45fr); gap:12px; align-items:start; padding:9px 14px; border-bottom:1px solid rgba(36,36,40,.72); font:10px/1.55 "Cascadia Mono",monospace; }
    .event-row:hover { background:rgba(255,255,255,.018); } .event-time { color:var(--text-tertiary); } .event-type { overflow:hidden; color:#aca3ff; text-overflow:ellipsis; white-space:nowrap; } .event-data { overflow-wrap:anywhere; color:var(--text-secondary); }
    .event-empty { padding:42px 16px; color:var(--text-tertiary); font-size:12px; text-align:center; }
    .fault-list { display:grid; gap:7px; margin-top:13px; }
    .fault-row { display:grid; grid-template-columns:minmax(0,1fr) auto; gap:10px; align-items:center; padding:8px 9px; border:1px solid rgba(237,106,120,.18); border-radius:6px; background:var(--red-soft); }
    .fault-row span { overflow:hidden; color:#e9a1a9; font:10px "Cascadia Mono",monospace; text-overflow:ellipsis; white-space:nowrap; }
    .hint { padding:9px 0; color:var(--text-tertiary); font-size:11px; }
    #validation-error { display:none; position:fixed; top:70px; right:24px; z-index:20; width:min(420px,calc(100% - 48px)); padding:12px 14px; border:1px solid rgba(237,106,120,.42); border-radius:7px; background:#261317; color:#ffc0c7; box-shadow:0 18px 60px rgba(0,0,0,.45); font-size:12px; }
    #validation-error.visible { display:block; animation:notice-in .18s ease-out; }
    @keyframes notice-in { from { opacity:0; transform:translateY(-6px); } to { opacity:1; transform:translateY(0); } }
    @media(max-width:1100px) { .state-panel,.events-panel { grid-column:span 12; } .runtime-panel,.scenario-panel,.fault-panel { grid-column:span 6; } .fault-panel { grid-column:auto / span 6; } }
    @media(max-width:760px) {
      .app-shell { display:block; } .workspace-sidebar { position:relative; width:100%; height:auto; padding:10px 14px; border-right:0; border-bottom:1px solid var(--line); }
      .brand { padding-bottom:9px; } .sidebar-label,.sidebar-spacer,.environment-card { display:none; } .sidebar-nav { display:flex; overflow-x:auto; } .nav-item { flex:0 0 auto; }
      .workspace-header { padding:0 16px; } .breadcrumb .optional { display:none; } .content { padding:22px 16px 36px; } .page-intro { display:block; } .device-chip { margin-top:16px; }
      .status-grid { grid-template-columns:1fr 1fr; } .runtime-panel,.scenario-panel,.fault-panel { grid-column:span 12; } .fault-panel { grid-column:auto / span 12; }
      .event-row { grid-template-columns:62px minmax(0,1fr); } .event-data { grid-column:1/-1; }
    }
    @media(max-width:460px) { .status-grid,.form-grid { grid-template-columns:1fr; } .form-grid .wide { grid-column:auto; } .header-meta span:last-child { display:none; } }
    @media(prefers-reduced-motion:reduce) { *,*::before,*::after { scroll-behavior:auto!important; animation:none!important; transition:none!important; } }
  </style>
</head>
<body>
  <div class="app-shell">
    <aside class="workspace-sidebar" aria-label="Workspace navigation">
      <div class="brand"><div class="brand-mark">IS</div><div class="brand-copy"><strong>Industrial Sim</strong><span>Developer runtime</span></div></div>
      <div class="sidebar-label">Workspace</div>
      <nav class="sidebar-nav">
        <a class="nav-item active" href="#overview">Overview</a><a class="nav-item" href="#state-store">State store</a><a class="nav-item" href="#runtime-events">Events</a><a class="nav-item" href="#scenario-control">Scenarios</a><a class="nav-item" href="#fault-control">Faults</a>
      </nav>
      <div class="sidebar-spacer"></div>
      <div class="environment-card"><span>Connected device</span><strong id="sidebar-device-id">Connecting…</strong><small id="sidebar-device-type">runtime discovery</small></div>
    </aside>
    <section class="command-center">
      <header class="workspace-header"><div class="breadcrumb"><strong>Workspace</strong><span class="slash">/</span><span>Runtime</span><span class="slash optional">/</span><span class="optional">Command center</span></div><div class="header-meta"><i class="sync-dot"></i><span id="last-sync">Connecting</span></div></header>
      <div id="validation-error" role="alert" aria-live="assertive"></div>
      <main id="overview" class="content">
        <section class="page-intro"><div><div class="eyebrow">Runtime operations</div><h1>Industrial Device Simulation</h1><p>Inspect shared state, coordinate scenarios, and inject controlled failures from one live operational workspace.</p></div><div class="device-chip"><div class="device-glyph">D1</div><div><strong id="device-id">Connecting…</strong><span id="device-type">runtime discovery</span></div></div></section>
        <section class="status-grid" aria-label="Runtime status" aria-live="polite">
          <div class="status-card"><div class="status-card-label"><span>Runtime</span><i id="runtime-lamp" class="lamp"></i></div><strong id="runtime-state">Loading</strong></div>
          <div class="status-card"><div class="status-card-label"><span>OPC UA</span><i id="opcua-lamp" class="lamp"></i></div><strong id="opcua-state">Unknown</strong></div>
          <div class="status-card"><div class="status-card-label"><span>Modbus TCP</span><i id="modbus-lamp" class="lamp"></i></div><strong id="modbus-state">Unknown</strong></div>
          <div class="status-card"><div class="status-card-label"><span>Activity</span><i id="activity-lamp" class="lamp idle"></i></div><strong id="activity-state">Idle</strong></div>
        </section>
        <div class="dashboard-grid">
          <section id="state-store" class="panel state-panel"><div class="panel-head"><div class="panel-title"><i class="panel-icon"></i><h2>StateStore datapoints</h2></div><small id="sim-time">00:00:00</small></div><div class="panel-body"><table><thead><tr><th>Signal</th><th>Type</th><th>Runtime value</th></tr></thead><tbody id="state-body"></tbody></table></div></section>
          <section class="panel runtime-panel"><div class="panel-head"><div class="panel-title"><i class="panel-icon"></i><h2>Runtime control</h2></div><small>Running · Paused · Stopped</small></div><div class="panel-body"><p class="runtime-copy">Control the simulation clock and lifecycle. Advance is available only in deterministic mode.</p><div class="controls"><button type="button" class="primary" data-runtime="start">Start / Resume</button><button type="button" data-runtime="pause">Pause</button><button type="button" class="danger" data-runtime="stop">Stop</button><button type="button" data-runtime="reset">Reset</button><button type="button" id="tick">Advance 1s</button></div></div></section>
          <section id="runtime-events" class="panel events-panel"><div class="panel-head"><div class="panel-title"><i class="panel-icon"></i><h2>Runtime events</h2></div><small>Commit ordered · latest 80</small></div><div id="event-terminal" class="event-terminal" aria-live="polite"><div class="event-empty">Waiting for runtime events…</div></div></section>
          <section id="scenario-control" class="panel scenario-panel"><div class="panel-head"><div class="panel-title"><i class="panel-icon"></i><h2>Scenario control</h2></div><small id="scenario-state">Stopped</small></div><div class="panel-body"><label for="scenario-yaml">Scenario YAML</label><textarea id="scenario-yaml" spellcheck="false">scenario:
  name: operator-sequence
  steps:
    - at: 0s
      set:
        device: pump-001
        datapoint: speed
        value: 900</textarea><div class="controls action-row"><button type="button" id="run-scenario" class="primary">Run Scenario</button><button type="button" id="stop-scenario" class="danger">Stop Scenario</button></div></div></section>
          <section id="fault-control" class="panel fault-panel"><div class="panel-head"><div class="panel-title"><i class="panel-icon"></i><h2>Fault control</h2></div><small>Data · Device · Network</small></div><div class="panel-body"><div class="form-grid"><div><label for="fault-category">Category</label><select id="fault-category"><option>Data</option><option>Device</option><option>Network</option></select></div><div><label for="fault-type">Type</label><input id="fault-type" value="spike"></div><div><label for="fault-target">Target</label><input id="fault-target" value="speed"></div><div><label for="fault-parameter">Parameter</label><input id="fault-parameter" value="25"></div><div class="wide"><button type="button" id="activate-fault" class="danger">Activate Fault</button></div></div><div id="fault-list" class="fault-list"><div class="hint">No active faults.</div></div></div></section>
        </div>
      </main>
    </section>
  </div>
  <script>
    const $ = id => document.getElementById(id);
    const request = async (url,options={}) => { const response=await fetch(url,options); const text=await response.text(); let data={}; try{data=text?JSON.parse(text):{}}catch{data={error:text}} if(!response.ok) throw new Error(data.error||`${response.status} ${response.statusText}`); return data; };
    const showError = error => { const box=$('validation-error'); box.textContent=error.message||String(error); box.classList.add('visible'); setTimeout(()=>box.classList.remove('visible'),6000); };
    const lamp = (id,state) => { $(id).className=`lamp ${String(state).toLowerCase()}`; };
    const node = (tag,text,className) => { const element=document.createElement(tag); if(text!==undefined) element.textContent=text; if(className) element.className=className; return element; };
    const valueType = value => value===null?'null':Array.isArray(value)?'array':typeof value;
    const renderState = state => { const body=$('state-body'); body.replaceChildren(); const entries=Object.entries(state); if(!entries.length){const row=node('tr');const cell=node('td','No datapoints');cell.colSpan=3;row.append(cell);body.append(row);return} entries.forEach(([name,value])=>{const row=node('tr');const type=node('span',valueType(value),'type-badge');const typeCell=node('td');typeCell.append(type);const valueCell=node('td');valueCell.append(node('code',JSON.stringify(value)));row.append(node('td',name),typeCell,valueCell);body.append(row)}); };
    const renderEvents = events => { const terminal=$('event-terminal'); terminal.replaceChildren(); if(!events.length){terminal.append(node('div','Waiting for runtime events…','event-empty'));return} events.slice(-80).reverse().forEach(event=>{const row=node('div',undefined,'event-row');const rawTime=event.timestamp?.elapsed||event.timestamp||event.time;const time=typeof rawTime==='string'&&rawTime.includes(':')?rawTime.split('.')[0]:rawTime?new Date(rawTime).toLocaleTimeString([], {hour12:false}):'--:--:--';const type=event.eventType||event.type||(event.dataPointId?'DataPointChanged':event.commandName?'CommandExecuted':'eventMetadata' in event?'Lifecycle':'RuntimeEvent');row.append(node('span',time,'event-time'),node('span',String(type),'event-type'),node('span',JSON.stringify(event),'event-data'));terminal.append(row)}); };
    const renderFaults = faults => { const list=$('fault-list'); list.replaceChildren(); if(!faults.length){list.append(node('div','No active faults.','hint'));return} const categories=['Data','Device','Network']; faults.forEach(fault=>{const row=node('div',undefined,'fault-row');const button=node('button','Recover');button.type='button';button.dataset.recover=String(fault.id);row.append(node('span',`${fault.id} · ${categories[fault.category]??fault.category} · ${fault.type}`),button);list.append(row)}); };
    const refresh = async () => { try {
      const [state,runtime,protocols,events,faults]=await Promise.all(['/api/state','/api/runtime','/api/protocols','/api/events','/api/faults'].map(url=>request(url)));
      $('device-id').textContent=runtime.deviceId; $('device-type').textContent=`${runtime.deviceType} · ${runtime.deterministic?'deterministic':'real-time'} · seed ${runtime.seed}`; $('sidebar-device-id').textContent=runtime.deviceId; $('sidebar-device-type').textContent=runtime.deviceType;
      $('runtime-state').textContent=runtime.state; lamp('runtime-lamp',runtime.state); $('sim-time').textContent=runtime.time;
      $('opcua-state').textContent=protocols.opcua?'Online':'Offline'; lamp('opcua-lamp',protocols.opcua?'running':'stopped'); $('modbus-state').textContent=protocols.modbus?'Online':'Offline'; lamp('modbus-lamp',protocols.modbus?'running':'stopped');
      const active=runtime.scenario.running||runtime.activeFaults>0; $('activity-state').textContent=runtime.scenario.running?`Scenario: ${runtime.scenario.name}`:runtime.activeFaults?`${runtime.activeFaults} Fault Active`:'Idle'; lamp('activity-lamp',active?'active':'idle'); $('scenario-state').textContent=runtime.scenario.running?'Running':'Stopped'; $('last-sync').textContent=`Synced ${new Date().toLocaleTimeString([], {hour12:false})}`;
      renderState(state); renderEvents(events); renderFaults(faults);
      document.querySelectorAll('[data-recover]').forEach(button=>button.onclick=async()=>{try{await request(`/api/fault/recover/${encodeURIComponent(button.dataset.recover)}`,{method:'POST'});await refresh()}catch(error){showError(error)}});
      if($('scenario-yaml').value.includes('pump-001')) $('scenario-yaml').value=$('scenario-yaml').value.replaceAll('pump-001',runtime.deviceId); $('tick').disabled=!runtime.deterministic;
    } catch(error){ showError(error); } };
    document.querySelectorAll('[data-runtime]').forEach(button=>button.onclick=async()=>{try{await request(`/api/runtime/${button.dataset.runtime}`,{method:'POST'});await refresh()}catch(error){showError(error)}});
    $('tick').onclick=async()=>{try{await request('/api/runtime/tick/1',{method:'POST'});await refresh()}catch(error){showError(error)}}; $('run-scenario').onclick=async()=>{try{await request('/api/scenario',{method:'POST',headers:{'Content-Type':'text/yaml'},body:$('scenario-yaml').value});await refresh()}catch(error){showError(error)}};
    $('stop-scenario').onclick=async()=>{try{await request('/api/scenario',{method:'DELETE'});await refresh()}catch(error){showError(error)}}; $('activate-fault').onclick=async()=>{try{const parameter=$('fault-parameter').value;await request('/api/fault',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({id:`ui-${Date.now()}`,category:$('fault-category').value,target:$('fault-target').value,type:$('fault-type').value,metadata:parameter?{parameter}:null})});await refresh()}catch(error){showError(error)}};
    refresh(); setInterval(refresh,1000);
  </script>
</body>
</html>
""";
}

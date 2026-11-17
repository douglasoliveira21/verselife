namespace LifeVerse.Api.Modules.Economy;

/// <summary>The page "Economia VerseCoin" of the administration (/admin/economia). The page itself holds no secret: the administrator types the access token and the MFA token, which stay in the page's memory and go in the headers of each call to the admin API (which checks them again). Everything that comes from the server is put in the page as text, never as HTML.</summary>
public static class EconomyAdminPage
{
    public static IEndpointRouteBuilder MapEconomyAdminPage(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/economia", () => Results.Content(Html, "text/html; charset=utf-8"));
        return endpoints;
    }

    private const string Html = """
<!doctype html><html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Economia VerseCoin</title>
<style>
:root{--bg:#0b1117;--panel:#1b262e;--line:#4e5d67;--text:#fff;--muted:#a9b3bd;--accent:#12f2e0;--orange:#f4a340;--danger:#ff6b6b;--ok:#3bf45f}
*{box-sizing:border-box}body{margin:0;font:15px/1.45 Inter,system-ui,sans-serif;background:var(--bg);color:var(--text)}
main{max-width:1100px;margin:0 auto;padding:28px 20px 60px}h1{font-size:26px;margin:0 0 4px}h2{font-size:15px;letter-spacing:.06em;text-transform:uppercase;color:var(--accent);margin:0 0 12px}
p.sub{color:var(--muted);margin:0 0 22px}.card{background:var(--panel);border:1px solid var(--line);border-radius:14px;padding:18px;margin-bottom:18px}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:12px}.stat{background:#232d36;border:1px solid var(--line);border-radius:10px;padding:12px 14px}
.stat b{display:block;font-size:22px;margin-top:2px}.stat span{color:var(--muted);font-size:13px}
input,button{font:inherit;border-radius:8px;border:1px solid var(--line);background:#2a3239;color:var(--text);padding:9px 12px}input{width:100%}
button{cursor:pointer;background:#252d33}button.primary{background:#0a3a3c;border-color:var(--accent)}button.warn{border-color:var(--orange)}button:disabled{opacity:.5;cursor:default}
.row{display:flex;gap:10px;flex-wrap:wrap;align-items:end;margin-bottom:10px}.row>label{flex:1;min-width:180px;color:var(--muted);font-size:13px}.row>label input{margin-top:4px}
table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:7px 8px;border-bottom:1px solid #2f3b44;font-size:14px}th{color:var(--muted);font-weight:500}
.in{color:var(--ok)}.out{color:var(--danger)}#msg{min-height:22px;color:var(--orange)}.muted{color:var(--muted)}
</style></head><body><main>
<h1>Economia VerseCoin</h1><p class="sub">Circulação, carteiras e ajustes auditados. Nada aqui muda um saldo sem motivo registrado.</p>
<div class="card"><h2>Acesso</h2><div class="row"><label>Token de acesso<input id="tok" type="password" autocomplete="off"></label><label>Token MFA<input id="mfa" type="password" autocomplete="off"></label><button class="primary" id="load">Carregar</button></div><div id="msg"></div></div>
<div class="card"><h2>Resumo</h2><div class="grid" id="stats"></div></div>
<div class="card"><h2>Carteira de um jogador</h2>
<div class="row"><label>Buscar por nome ou e-mail<input id="q" placeholder="nome do jogador"></label><button id="find">Buscar</button></div><div id="found" class="muted"></div>
<div id="wallet" hidden><p>Jogador: <b id="wname"></b> · saldo <b id="wbal"></b> · <span id="wstate"></span></p>
<div class="row"><label>Motivo (obrigatório)<input id="reason" maxlength="200"></label><label>Valor do ajuste (negativo retira)<input id="amount" type="number" step="1"></label></div>
<div class="row"><button class="warn" id="block">Bloquear / desbloquear carteira</button><button class="primary" id="adjust">Aplicar ajuste</button></div>
<table><thead><tr><th>Data</th><th>Tipo</th><th>Valor</th><th>Situação</th></tr></thead><tbody id="rows"></tbody></table></div></div>
</main><script>
const $=id=>document.getElementById(id);let current=null,blocked=false;
const fmt=n=>'VC$ '+Number(n).toLocaleString('pt-BR');
const say=t=>{$('msg').textContent=t};
async function api(path,method='GET',body){const r=await fetch('/api/v1/admin/'+path,{method,headers:{'Authorization':'Bearer '+$('tok').value,'X-Admin-MFA':$('mfa').value,'Content-Type':'application/json'},body:body?JSON.stringify(body):undefined});
 if(!r.ok){let e='';try{e=(await r.json()).error||''}catch{}throw new Error('Erro '+r.status+(e?' ('+e+')':''))}return r.status===204?null:r.json()}
function stat(label,value){const d=document.createElement('div');d.className='stat';const s=document.createElement('span');s.textContent=label;const b=document.createElement('b');b.textContent=value;d.append(s,b);return d}
async function summary(){const s=await api('economy/summary');const box=$('stats');box.replaceChildren(
 stat('Moedas nas carteiras',fmt(s.coinsInWallets)),stat('Carteiras',s.wallets),stat('Carteiras bloqueadas',s.blockedWallets),stat('Moedas vendidas (total)',fmt(s.coinsSoldTotal)),
 stat('Moedas estornadas (total)',fmt(s.coinsRefundedTotal)),stat('Ajustes da administração',fmt(s.adminAdjustmentsTotal)),stat('Transações em 24 h',s.transactionsLast24h),stat('Volume em 24 h',fmt(s.volumeLast24h)),
 stat('Pedidos pendentes',s.pendingCoinOrders),stat('Pedidos em revisão',s.ordersInReview),stat('Receita (R$)',(s.revenueCentsTotal/100).toLocaleString('pt-BR',{minimumFractionDigits:2})))}
async function showWallet(u){const w=await api('economy/wallets/'+u.id);current=u;blocked=w.isBlocked;$('wallet').hidden=false;$('wname').textContent=u.username;$('wbal').textContent=fmt(w.availableCoins);$('wstate').textContent=blocked?'BLOQUEADA':'ativa';
 const rows=$('rows');rows.replaceChildren();for(const t of w.recent){const tr=document.createElement('tr');const cells=[new Date(t.createdAt).toLocaleString('pt-BR'),t.type,(t.direction==='in'?'+ ':'− ')+fmt(Math.abs(t.amount)),t.status];
  cells.forEach((c,i)=>{const td=document.createElement('td');td.textContent=c;if(i===2)td.className=t.direction==='in'?'in':'out';tr.append(td)});rows.append(tr)}}
$('load').onclick=async()=>{try{say('Carregando…');await summary();say('')}catch(e){say(e.message)}};
$('find').onclick=async()=>{try{const list=await api('users?q='+encodeURIComponent($('q').value));const f=$('found');f.replaceChildren();if(!list.length){f.textContent='Ninguém encontrado.';return}
 for(const u of list.slice(0,8)){const b=document.createElement('button');b.textContent=u.username;b.style.margin='0 6px 6px 0';b.onclick=async()=>{try{await showWallet(u);say('')}catch(e){say(e.message)}};f.append(b)}}catch(e){say(e.message)}};
$('block').onclick=async()=>{if(!current)return;try{await api('economy/wallets/'+current.id+'/block','POST',{blocked:!blocked,reason:$('reason').value});await showWallet(current);say('Carteira '+(blocked?'bloqueada.':'desbloqueada.'))}catch(e){say(e.message)}};
$('adjust').onclick=async()=>{if(!current)return;const amount=parseInt($('amount').value,10);if(!amount){say('Informe um valor diferente de zero.');return}
 if(!confirm('Ajustar '+fmt(amount)+' na carteira de '+current.username+'?'))return;try{const key=crypto.randomUUID().replaceAll('-','');await api('economy/wallets/'+current.id+'/adjust','POST',{amount,reason:$('reason').value,idempotencyKey:key});$('amount').value='';await showWallet(current);await summary();say('Ajuste aplicado e auditado.')}catch(e){say(e.message)}};
</script></body></html>
""";
}

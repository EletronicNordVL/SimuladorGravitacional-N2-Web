/*  Funções e variáveis globais para o simulador gravitacional em JavaScript, incluindo manipulação de DOM, controle de simulação, renderização de corpos e rastros, e comunicação com a API do servidor. */
'use strict';

/* Função auxiliar para obter elementos do DOM pelo ID. */
const $ = id => document.getElementById(id);
const canvas = $('universe-canvas');
const ctx = canvas.getContext('2d');
const colors = ['#dfba78', '#a9bf8b', '#7baea3', '#b2a2c8', '#d29176', '#8ca8c4', '#d2cb98', '#c69bae'];
const numberFormat = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 2 });

/* Variáveis globais para armazenar o estado da simulação, configurações de visualização, rastros dos corpos e controle de execução. */
let state, initialState, selected = 0, running = false, busy = false, transition = false;
let width = 1, height = 1, zoom = 1, center = { x: 0, y: 0 }, extent = 7e9;
let trails = [], editing = null, timer = null, toastTimer = null, dragging = null;
let moved = false, lastListUpdate = 0;

/* Dados do replay em andamento (null quando a tela está em simulação normal) como o id da simulação gravada, o
 * último passo gravado, passo atual e os quadros já baixados do banco que ainda não foram exibidos. */
let replay = null;

/* Função auxiliar para clonar objetos de forma profunda, garantindo que alterações no estado atual não afetem o estado inicial. */
const clone = value => structuredClone(value);
const color = index => colors[index % colors.length];
const sci = value => {
    if (!Number.isFinite(value)) return '—';
    if (value === 0) return '0';
    if (Math.abs(value) >= 1e5 || Math.abs(value) < .01) {
        const [mantissa, exponent] = value.toExponential(2).split('e');
        return `${mantissa.replace('.', ',')}e${Number(exponent)}`;
    }
    return numberFormat.format(value);
};

/* Função para fazer requisições API. */
async function api(path, data, method = 'POST') {
    const response = await fetch(path, { method, headers: data === undefined ? {} : { 'Content-Type': 'application/json' }, body: data === undefined ? undefined : JSON.stringify(data) });
    if (!response.ok) {
        let message = 'Não foi possível concluir a operação.';
        try { const error = await response.json(); message = error.error || error.detail || message; } catch { /* Resposta sem JSON. */ }
        throw new Error(message);
    }
    /* Retorna null para respostas 204 (No Content) e o JSON para outras respostas bem-sucedidas. */
    return response.status === 204 ? null : response.json();
}

/* Função para exibir mensagens temporárias na tela. */
function toast(message, error = false) {
    clearTimeout(toastTimer);
    $('toast').textContent = message;
    $('toast').classList.toggle('error', error);
    $('toast').hidden = false;
    toastTimer = setTimeout(() => { $('toast').hidden = true; }, error ? 7000 : 4000);
}

/* Função para exibir um diálogo de confirmação e aguardar a resposta do usuário. */
function confirmAction(title, message) {
    pause();
    $('confirm-title').textContent = title;
    $('confirm-message').textContent = message;
    const dialog = $('confirm-dialog');
    dialog.showModal();
    return new Promise(resolve => {
        let result = false;
        const accept = () => { result = true; dialog.close(); };
        const cancel = () => dialog.close();
        const finish = () => {
            $('confirm-ok').removeEventListener('click', accept);
            $('confirm-cancel').removeEventListener('click', cancel);
            dialog.removeEventListener('close', finish);
            resolve(result);
        };
        $('confirm-ok').addEventListener('click', accept);
        $('confirm-cancel').addEventListener('click', cancel);
        dialog.addEventListener('close', finish);
    });
}

/* Função para habilitar ou desabilitar os controles da interface com base no estado atual da simulação. */
function setControls() {
    const blocked = !state || busy || transition;
    for (const id of ['play-button', 'step-button', 'reset-button', 'save-button', 'add-body', 'edit-body', 'library-button'])
        $(id).disabled = blocked;

    /* Novo universo e Gerar corpos aleatórios não precisam ser
     * desabilitados durante cada requisição de uma iteração.
     * O bloqueio durante a troca de universo continua sendo mantido. */
    for (const id of ['generate-button', 'new-button', 'retry-button'])
        $(id).disabled = !state ? id !== 'retry-button' : transition;

    /* Pausar deve continuar disponível durante a requisição de um passo. */
    $('play-button').disabled = !state || transition;
    $('step-button').disabled = blocked || running || state?.iteration >= state?.quantidadeIteracoes;
    $('iterations').disabled = running || busy || transition;
    $('timestep').disabled = running || busy || transition;

    /* No replay só é possível reproduzir o que foi gravado: não há edição, salvamento nem mudança de parâmetros. */
    if (replay) {
        for (const id of ['save-button', 'add-body', 'edit-body', 'iterations', 'timestep', 'record'])
            $(id).disabled = true;
    } else $('record').disabled = running || busy || transition;
}

/* Função para atualizar o status da simulação e os controles da interface. */
function updateStatus() {
    const completed = state && state.iteration >= state.quantidadeIteracoes;
    $('status').textContent = (replay ? 'REPLAY · ' : '') + (running ? 'EM MOVIMENTO' : completed ? 'CONCLUÍDO' : 'PAUSADO');
    $('status').classList.toggle('running', running);
    $('play-button').replaceChildren();
    const icon = document.createElement('span');
    icon.setAttribute('aria-hidden', 'true');
    icon.textContent = running ? 'Ⅱ' : '▶';
    $('play-button').append(icon, document.createTextNode(running ? 'Pausar simulação' : completed ? 'Reiniciar simulação' : 'Iniciar simulação'));
    setControls();
}

/* Função para pausar a simulação e atualizar o status da interface. */
function pause() {
    running = false;
    clearTimeout(timer);
    updateStatus();
}

/* Função para aplicar um novo estado à simulação, redefinir as condições iniciais e atualizar a interface. */
function applyState(next, title = 'Universo personalizado') {
    pause();
    replay = null; /* Qualquer novo estado (gerar, abrir, importar) encerra o modo replay. */
    state = next; /* O estado atual é substituído pelo novo estado fornecido. */
    initialState = clone(next); /* O estado inicial é atualizado para o novo estado, permitindo reinicializações. */
    selected = 0; /* O corpo selecionado é redefinido para o primeiro corpo da lista. */
    trails = state.corpos.map(c => [{ x: c.posX, y: c.posY }]); /* Os rastros dos corpos são reiniciados com base nas posições iniciais do novo estado. */
    $('universe-title').textContent = title;
    $('iterations').value = state.quantidadeIteracoes;
    $('timestep').value = state.tempoEntreIteracoes;
    $('viewport-error').hidden = true;
    fit(); /* Ajusta a visualização para caber todos os corpos na tela. */
    updateUI(true); /* Atualiza a interface do usuário com base no novo estado, incluindo métricas, lista de corpos e detalhes do corpo selecionado. */
}

/* Função para gerar um novo universo aleatório, com confirmação do usuário se houver um estado existente. */
async function generate(ask = true) {
    if (busy || transition) return;
    if (ask && state && !await confirmAction('Gerar outro universo?', 'O estado atual será substituído. Salve ou exporte o universo antes de continuar se quiser guardá-lo.')) return;
    pause();
    transition = true;
    setControls();

/* Bloco de Try-Catch-Finally para lidar com a geração do universo aleatório, incluindo validação da quantidade de corpos e atualização do estado da simulação. */
    try {
        const count = Number($('body-count').value);
        const iterations = Number($('iterations').value);
        const timestep = Number($('timestep').value);

        /* Valida os parâmetros de entrada para a geração do universo aleatório, garantindo que a quantidade de corpos, 
iterações e passo de tempo estejam dentro dos limites aceitáveis. */
        if (!Number.isInteger(count) || count < 1)
            throw new Error('A quantidade de corpos deve ser maior que zero.');

/* Valida a quantidade de iterações, garantindo que esteja entre 1 e 1.000.000. */
        if (!Number.isInteger(iterations) || iterations < 1 || iterations > 1000000)
            throw new Error('Use de 1 a 1.000.000 iterações.');

/* Valida o passo de tempo, garantindo que seja finito e esteja entre 0,001 e 86.400 segundos. */
        if (!Number.isFinite(timestep) || timestep < .001 || timestep > 86400)
            throw new Error('O passo de tempo deve estar entre 0,001 e 86.400 segundos.');

/* Faz uma requisição à API para criar um novo universo aleatório com os parâmetros validados, 
recebendo o estado inicial do universo criado. */
        const next = await api('/api/universes/create', {
            count,
            iterations,
            timestep
        });

/* Aplica o novo estado à simulação, atualizando a interface e redefinindo as condições iniciais. */
        applyState(next, 'Universo aleatório');
    } catch (error) {
        toast(error.message, true);
        if (!state) $('viewport-error').hidden = false;
    } finally { transition = false; setControls(); }
}

/* Função para ler as configurações de iterações e passo de tempo da interface, validá-las e aplicá-las ao estado da simulação. */
function readSettings() {
    const iterations = Number($('iterations').value), timestep = Number($('timestep').value);
    if (!Number.isInteger(iterations) || iterations < 1 || iterations > 1000000 || iterations < state.iteration) throw new Error('O limite deve ser inteiro, entre 1 e 1.000.000, e não menor que a iteração atual.');
    if (!Number.isFinite(timestep) || timestep < .001 || timestep > 86400) throw new Error('O passo de tempo deve estar entre 0,001 e 86.400 segundos.');
    state.quantidadeIteracoes = iterations;
    state.tempoEntreIteracoes = timestep;
}

/* Função para avançar a simulação em um número específico de passos, atualizando o estado e a interface. */
async function advance(steps = 1) {
    if (busy || transition || !state || state.iteration >= state.quantidadeIteracoes) return;
    busy = true;
    setControls();
    try {
        /* No replay, os passos vêm do banco de dados em vez de serem calculados. */
        if (replay) { await replayAdvance(steps); return; }
        readSettings();
        const next = await api('/api/simulation/step', { state, steps });
        state = next;
        state.corpos.forEach((body, index) => {
            if (!trails[index]) trails[index] = [];
            trails[index].push({ x: body.posX, y: body.posY });
            if (trails[index].length > 800) trails[index].shift();
        });

/* Atualiza a interface do usuário, incluindo métricas, lista de corpos e detalhes do corpo selecionado, se necessário. */
        updateUI(performance.now() - lastListUpdate > 400);
        if (state.iteration >= state.quantidadeIteracoes) { pause(); toast('Simulação concluída. Você pode salvar o resultado ou reiniciar.'); }
    } catch (error) { pause(); toast(error.message, true); }
    finally { busy = false; setControls(); }
}

/* Se a opção "Gravar iterações no banco" estiver ligada, cria a simulação no MySQL (estado inicial = passo 0) antes da
 * primeira iteração. A partir daí, cada chamada de /api/simulation/step grava os passos no banco.
 * Se o banco estiver indisponível, a simulação segue normalmente, só que sem gravar. */
async function ensureRecording() {
    if (!$('record').checked || replay || state.idSimulacao > 0 || state.iteration !== 0) return;
    try {
        const saved = await api('/api/universes/save', state);
        state.idSimulacao = saved.id;
        /* Atualiza os IDs dos corpos com os registros criados no banco. */
        state.corpos.forEach((corpo, index) => {
            corpo.idCorpoBanco = saved.corpoIds[index];
        });
        toast('Gravando cada iteração no banco de dados.');
    } catch (error) {
        $('record').checked = false;
        toast(`${error.message} A simulação seguirá sem gravar no banco.`, true);
    }
}

/* Abre o replay de uma simulação gravada: carrega o estado inicial (passo 0) e deixa os passos seguintes para serem
 * buscados em blocos no banco enquanto o replay é reproduzido. */
async function openReplay(file) {
    const meta = await api(`/api/replay/${file.id}`, undefined, 'GET');
    if (meta.maxPasso < 1) throw new Error('Esta simulação tem só o estado inicial gravado. Rode uma simulação com "Gravar iterações no banco" ligado.');
    meta.state.quantidadeIteracoes = meta.maxPasso;
    applyState(meta.state, `Replay — ${file.name}`);
    replay = { id: file.id, max: meta.maxPasso, pos: 0, buffer: new Map() };
    updateUI(true);
    toast(`Replay carregado: ${meta.maxPasso.toLocaleString('pt-BR')} iterações gravadas. Pressione ▶ para reproduzir.`);
}

/* Busca no banco o próximo bloco de passos. Quanto mais corpos, menos passos por bloco, para o bloco não ficar enorme. */
async function replayFetch(inicio) {
    const quantidade = Math.max(10, Math.min(300, Math.floor(8000 / state.corpos.length)));
    const quadros = await api(`/api/replay/${replay.id}/frames?inicio=${inicio}&quantidade=${quantidade}`, undefined, 'GET');
    for (const quadro of quadros) replay.buffer.set(quadro.passo, quadro);
}

/* Avança o replay em "steps" passos onde ele aplica ao estado atual 
as posições e velocidades gravadas e atualiza as trajetórias. */
async function replayAdvance(steps) {
    const alvo = Math.min(replay.pos + steps, replay.max);
    for (let passo = replay.pos + 1; passo <= alvo; passo++) {
        if (!replay.buffer.has(passo)) await replayFetch(passo);
        const quadro = replay.buffer.get(passo);
        if (!quadro || quadro.corpos.length !== state.corpos.length) throw new Error('Passo não encontrado no banco de dados.');
        state.iteration = passo;
        state.elapsed = quadro.tempo;
/* Atualiza as posições e velocidades dos corpos com base nos dados do quadro do replay, 
mantendo os rastros das trajetórias. */
        quadro.corpos.forEach(([posX, posY, velX, velY], index) => {
            Object.assign(state.corpos[index], { posX, posY, velX, velY });
            if (!trails[index]) trails[index] = [];
            trails[index].push({ x: posX, y: posY });
            if (trails[index].length > 800) trails[index].shift();
        });
/* Remove o quadro do buffer após aplicá-lo, liberando memória, e atualiza a posição atual do replay. */
        replay.buffer.delete(passo);
        replay.pos = passo;
    }
/* Atualiza a interface do usuário, incluindo métricas, lista de corpos e detalhes do corpo selecionado, se necessário. */
    updateUI(performance.now() - lastListUpdate > 400);
    if (replay.pos >= replay.max) { pause(); toast('Replay concluído.'); }
}

/*  Função de loop assíncrono que avança a simulação continuamente enquanto estiver em execução, respeitando a velocidade definida pelo usuário. */
async function loop() {
    if (!running) return;
    await advance(Number($('speed').value));
    if (running) timer = setTimeout(loop, 40);
}

/* Função para iniciar a simulação, alternando entre iniciar, pausar e reiniciar com base no estado atual. */
async function play() {
    if (!state || transition) return;
    if (running) { pause(); return; }
    if (busy) return;
    try { if (!replay) readSettings(); } catch (error) { toast(error.message, true); return; }
    if (state.iteration >= state.quantidadeIteracoes) reset();

    /* Cria a simulação no banco (se a gravação estiver ligada). O busy evita cliques duplicados enquanto isso acontece. */
    busy = true; setControls();
    try { await ensureRecording(); } finally { busy = false; }
    running = true;
    updateStatus();
    loop();
}

/* Função para redefinir a simulação para o estado inicial, pausando-a e atualizando a interface. */
function reset() {
    if (busy || transition || !initialState) return; /* Se não houver estado inicial, não há como reiniciar. */
    pause();
    if (replay) { replay.pos = 0; replay.buffer.clear(); } /* O replay volta ao passo 0 imediatamente. */
    state = clone(initialState);
    trails = state.corpos.map(c => [{ x: c.posX, y: c.posY }]);
    $('iterations').value = state.quantidadeIteracoes;
    $('timestep').value = state.tempoEntreIteracoes;
    fit(); updateUI(true);
}

/*  Função para atualizar a interface do usuário com base no estado atual da simulação, incluindo métricas, lista de corpos e detalhes do corpo selecionado. */
function updateUI(refreshList = false) {
    if (!state) return;
    $('metric-count').textContent = state.corpos.length;
    $('metric-time').textContent = numberFormat.format(state.elapsed / 86400);
    $('metric-mass').textContent = sci(state.corpos.reduce((sum, c) => sum + c.massa, 0));
    $('iteration-current').textContent = state.iteration.toLocaleString('pt-BR');
    $('iteration-total').textContent = `/ ${state.quantidadeIteracoes.toLocaleString('pt-BR')}`;
    $('progress').max = state.quantidadeIteracoes;
    $('progress').value = state.iteration;
    $('body-badge').textContent = state.corpos.length;
    if (refreshList) { renderBodyList(); lastListUpdate = performance.now(); }
    renderDetails();
    updateStatus();
}

/* Função para renderizar a lista de corpos na interface, mantendo os botões durante a reprodução para preservar o foco e a seleção. */
function renderBodyList() {
    const list = $('body-list');
    if (list.children.length !== state.corpos.length || !list.firstElementChild?.classList.contains('body-item')) {
        list.replaceChildren();
        state.corpos.forEach((body, index) => {
            const button = document.createElement('button');
            button.className = 'body-item';
            const dot = document.createElement('span'); dot.className = 'body-dot'; dot.style.setProperty('--body-color', color(index));
            const content = document.createElement('div'); content.append(document.createElement('strong'), document.createElement('small'));
            const arrow = document.createElement('span'); arrow.textContent = '›'; arrow.setAttribute('aria-hidden', 'true');
            button.append(dot, content, arrow);
            button.addEventListener('click', () => { selected = index; renderBodyList(); renderDetails(); });
            list.append(button);
        });
    }

/* Atualiza a seleção e os detalhes do corpo na lista, destacando o corpo selecionado e exibindo seu nome e massa. */
    state.corpos.forEach((body, index) => {
        const button = list.children[index];
        button.classList.toggle('selected', index === selected);
        button.setAttribute('aria-pressed', String(index === selected));
        button.querySelector('strong').textContent = body.nome;
        button.querySelector('small').textContent = `${sci(body.massa)} kg`;
    });
}

/* Função para renderizar os detalhes do corpo selecionado na interface, incluindo nome, massa, densidade, raio, velocidade e posição. */
function renderDetails() {
    const body = state?.corpos[selected];
    $('body-details').hidden = !body;
    if (!body) return;
    $('detail-name').textContent = body.nome;
    $('detail-mass').textContent = `${sci(body.massa)} kg`;
    $('detail-density').textContent = `${sci(body.densidade)} kg/m³`;
    $('detail-radius').textContent = `${sci(body.raio)} m`;
    $('detail-velocity').textContent = `${sci(Math.hypot(body.velX, body.velY))} m/s`;
    $('detail-x').textContent = `${sci(body.posX)} m`;
    $('detail-y').textContent = `${sci(body.posY)} m`;
}

/* Função para ajustar a visualização da simulação para caber todos os corpos na tela, centralizando e dimensionando a visualização de acordo com as posições dos corpos. */
function fit() {
    if (!state) return;
    const xs = state.corpos.map(c => c.posX), ys = state.corpos.map(c => c.posY);
    const minX = Math.min(...xs), maxX = Math.max(...xs), minY = Math.min(...ys), maxY = Math.max(...ys);
    center = { x: minX / 2 + maxX / 2, y: minY / 2 + maxY / 2 };
    extent = Math.max((maxX - minX) / Math.max(width / height, .2), maxY - minY, 3e8) * 1.45;
    zoom = 1;
}

/* Funções auxiliares para converter entre coordenadas do mundo e da tela, calcular a escala de pixels e determinar o raio visual de um corpo com base em sua massa. */
function pixelScale() { return height / extent * zoom; }
function toScreen(x, y) { const scale = pixelScale(); return { x: width / 2 + (x - center.x) * scale, y: height / 2 - (y - center.y) * scale }; }
function toWorld(x, y) { const scale = pixelScale(); return { x: center.x + (x - width / 2) / scale, y: center.y - (y - height / 2) / scale }; }
function visualRadius(body) { return Math.min(25, Math.max(4, Math.log10(body.massa) * 1.2 - 22)); }

/* Função de desenho principal que limpa o canvas, desenha o fundo, a grade, os rastros dos corpos e os próprios corpos com efeitos visuais, atualizando a cada frame. */
function draw() {
    ctx.clearRect(0, 0, width, height);
    for (let i = 0; i < 100; i++) {
        const x = ((i * 137.508) % 997) / 997 * width;
        const y = ((i * 229.31 + 19) % 991) / 991 * height;
        ctx.fillStyle = i % 7 === 0 ? '#9cae9635' : '#9cae9618';
        ctx.fillRect(x, y, i % 7 === 0 ? 1.5 : 1, i % 7 === 0 ? 1.5 : 1);
    }
    /* Função de desenho principal que limpa o canvas, desenha o fundo, a grade, os rastros dos corpos e os próprios corpos com efeitos visuais, atualizando a cada frame. */
    if (state) {
        if ($('grid').checked) drawGrid();
        if ($('trails').checked) trails.forEach((points, index) => {
            if (points.length < 2) return;
            ctx.beginPath(); ctx.strokeStyle = `${color(index)}65`; ctx.lineWidth = 1;
            points.forEach((point, j) => { const p = toScreen(point.x, point.y); if (j === 0) ctx.moveTo(p.x, p.y); else ctx.lineTo(p.x, p.y); });
            ctx.stroke();
        });

        /*  Função de desenho principal que limpa o canvas, desenha o fundo, a grade, os rastros dos corpos e os próprios corpos com efeitos visuais, atualizando a cada frame. */
        state.corpos.forEach((body, index) => {
            const p = toScreen(body.posX, body.posY), radius = visualRadius(body);
            if (p.x < -100 || p.x > width + 100 || p.y < -100 || p.y > height + 100) return;
            const glow = ctx.createRadialGradient(p.x, p.y, radius * .2, p.x, p.y, radius * 4);
            glow.addColorStop(0, `${color(index)}35`); glow.addColorStop(1, `${color(index)}00`);
            ctx.fillStyle = glow; ctx.beginPath(); ctx.arc(p.x, p.y, radius * 4, 0, Math.PI * 2); ctx.fill();
            if (index === selected) {
                ctx.strokeStyle = `${color(index)}55`; ctx.lineWidth = 1; ctx.setLineDash([3, 4]);
                ctx.beginPath(); ctx.arc(p.x, p.y, radius + 8, 0, Math.PI * 2); ctx.stroke(); ctx.setLineDash([]);
            }
            const sphere = ctx.createRadialGradient(p.x - radius * .3, p.y - radius * .3, 0, p.x, p.y, radius);
            sphere.addColorStop(0, '#f5f0dc'); sphere.addColorStop(.25, color(index)); sphere.addColorStop(1, `${color(index)}85`);
            ctx.fillStyle = sphere; ctx.beginPath(); ctx.arc(p.x, p.y, radius, 0, Math.PI * 2); ctx.fill();
            if ($('names').checked) { ctx.font = '10px "Segoe UI", sans-serif'; ctx.textAlign = 'left'; ctx.fillStyle = `${color(index)}cc`; ctx.fillText(body.nome, p.x + radius + 12, p.y + 4); }
        });
        $('scale-label').textContent = `${sci(70 / pixelScale())} m`;
    }

/* Solicita o próximo frame de animação, garantindo que a função de desenho seja chamada repetidamente para atualizar a visualização. */
    requestAnimationFrame(draw);
}

/* Função para desenhar a grade de referência no canvas, ajustando o espaçamento das linhas com base na escala atual da visualização. */
function drawGrid() {
    const scale = pixelScale(), raw = 65 / scale;
    const base = 10 ** Math.floor(Math.log10(raw)), ratio = raw / base;
    const step = (ratio < 2 ? 2 : ratio < 5 ? 5 : 10) * base;
    const left = toWorld(0, height), right = toWorld(width, 0);
    ctx.strokeStyle = '#70897410'; ctx.lineWidth = 1; ctx.beginPath();
    for (let x = Math.ceil(left.x / step) * step, n = 0; x <= right.x && n < 100; x += step, n++) { const p = toScreen(x, 0); ctx.moveTo(p.x, 0); ctx.lineTo(p.x, height); }
    for (let y = Math.ceil(left.y / step) * step, n = 0; y <= right.y && n < 100; y += step, n++) { const p = toScreen(0, y); ctx.moveTo(0, p.y); ctx.lineTo(width, p.y); }
    ctx.stroke();
    const origin = toScreen(0, 0); ctx.strokeStyle = '#819b7328'; ctx.setLineDash([2, 6]); ctx.beginPath();
    ctx.moveTo(origin.x, 0); ctx.lineTo(origin.x, height); ctx.moveTo(0, origin.y); ctx.lineTo(width, origin.y); ctx.stroke(); ctx.setLineDash([]);
}

/*  Observador de redimensionamento que ajusta o tamanho do canvas e a escala de pixels com base nas dimensões do elemento "viewport", garantindo que a visualização se adapte às mudanças de tamanho da janela. */
new ResizeObserver(entries => {
    const rect = entries[0].contentRect;
    width = rect.width; height = rect.height;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    canvas.width = Math.round(width * dpr); canvas.height = Math.round(height * dpr); ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    if (state && zoom === 1 && !dragging) fit();
}).observe($('viewport'));

/* Observadores de eventos para interações do usuário, incluindo rolagem do mouse para zoom, arrastar para mover a visualização e clicar para selecionar corpos. */
canvas.addEventListener('wheel', event => {
    event.preventDefault();
    const rect = canvas.getBoundingClientRect(), x = event.clientX - rect.left, y = event.clientY - rect.top;
    const before = toWorld(x, y);
    zoom = Math.min(100, Math.max(.03, zoom * Math.exp(-event.deltaY * .001)));
    const after = toWorld(x, y);
    center.x += before.x - after.x; center.y += before.y - after.y;
}, { passive: false });

/* Observador de eventos para iniciar o arrasto da visualização quando o usuário pressiona o botão esquerdo do 
mouse, armazenando a posição inicial do ponteiro e a posição central atual. */
canvas.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    dragging = { x: event.clientX, y: event.clientY, center: { ...center } }; moved = false;
    canvas.setPointerCapture(event.pointerId);
});

/* Observador de eventos para mover a visualização enquanto o usuário arrasta o mouse, atualizando a posição central com base no deslocamento do ponteiro. */
canvas.addEventListener('pointermove', event => {
    if (!dragging) return;
    const dx = event.clientX - dragging.x, dy = event.clientY - dragging.y;
    if (Math.hypot(dx, dy) > 4) moved = true;
    center.x = dragging.center.x - dx / pixelScale(); center.y = dragging.center.y + dy / pixelScale();
});

/* Observador de eventos para liberar o arrasto e selecionar corpos ao soltar o botão do mouse, se não houver movimento significativo. */
canvas.addEventListener('pointerup', event => {
    if (!dragging) return;
    if (!moved && state) {
        const rect = canvas.getBoundingClientRect(), x = event.clientX - rect.left, y = event.clientY - rect.top;
        const candidates = state.corpos.map((body, index) => ({ index, body, p: toScreen(body.posX, body.posY) }));
        const hit = candidates.filter(c => Math.hypot(c.p.x - x, c.p.y - y) < visualRadius(c.body) + 12).sort((a, b) => Math.hypot(a.p.x - x, a.p.y - y) - Math.hypot(b.p.x - x, b.p.y - y))[0];
        if (hit) { selected = hit.index; renderBodyList(); renderDetails(); }
    }
/* Observador de eventos para liberar o arrasto da visualização quando o usuário solta o botão do mouse, garantindo que a interação seja concluída corretamente. */
    dragging = null;
});

/* Observador de eventos para cancelar o arrasto da visualização se o ponteiro for cancelado, 
garantindo que a interação seja interrompida corretamente. */
canvas.addEventListener('pointercancel', () => { dragging = null; });

/* Função para abrir o diálogo de edição ou adição de um corpo, preenchendo os campos com os valores atuais do corpo selecionado ou com valores padrão para um novo corpo. */
function openBody(index = null) {
    if (busy || transition || !state) return;
    pause(); editing = index;
    const body = index === null ? { nome: `Corpo ${state.corpos.length + 1}`, massa: 5e24, densidade: 4500, posX: 3e9, posY: 0, velX: 0, velY: 100 } : state.corpos[index];
    const fields = { name: 'nome', mass: 'massa', density: 'densidade', x: 'posX', y: 'posY', vx: 'velX', vy: 'velY' };
    Object.entries(fields).forEach(([field, key]) => { $(`body-${field}`).value = body[key]; });
/* Atualiza o título do diálogo, limpa mensagens de erro, habilita ou desabilita o botão de remoção 
com base na quantidade de corpos e exibe o diálogo para edição ou adição do corpo. */
    $('body-dialog-title').textContent = index === null ? 'Adicionar corpo' : 'Editar corpo';
    $('body-form-error').textContent = '';
    $('remove-body').hidden = index === null;
    $('remove-body').disabled = state.corpos.length <= 1;
    $('remove-body').title = state.corpos.length <= 1 ? 'O universo precisa ter pelo menos um corpo.' : 'Remover este corpo';
    $('body-dialog').showModal();
}

/* Função para confirmar a edição ou adição de um corpo, validando os campos e atualizando o estado da simulação com as novas informações do corpo. */
function commitEdit() {
    state.iteration = 0; state.elapsed = 0; /* Reinicia a simulação, pois as condições iniciais mudaram. */
    state.idSimulacao = 0; /* Corpos editados: a gravação em andamento não vale mais; a próxima simulação cria outra. */
    initialState = clone(state); /* Atualiza o estado inicial para refletir as alterações feitas nos corpos. */
    trails = state.corpos.map(c => [{ x: c.posX, y: c.posY }]); /* Reinicia os rastros dos corpos com base nas novas posições iniciais. */
    $('universe-title').textContent = 'Universo personalizado'; 
    fit(); updateUI(true); /* Atualiza a interface do usuário para refletir as alterações feitas nos corpos, incluindo a lista de corpos e os detalhes do corpo selecionado. */
}

/* Observador de envio do formulário de corpo, que lê os valores dos campos, valida-os e atualiza o estado da simulação com as informações do corpo adicionado ou editado. */
$('body-form').addEventListener('submit', event => {
    event.preventDefault();
    const parse = id => Number($(id).value.trim().replace(',', '.'));
    const body = { nome: $('body-name').value.trim(), massa: parse('body-mass'), densidade: parse('body-density'), posX: parse('body-x'), posY: parse('body-y'), velX: parse('body-vx'), velY: parse('body-vy'), forcaX: 0, forcaY: 0, aceleracaoX: 0, aceleracaoY: 0 };
    try {
        readSettings();
        if (!body.nome || body.nome.length > 60 || /[;\r\n]/.test(body.nome)) throw new Error('Informe um nome válido, sem ponto e vírgula.');
        if (!Number.isFinite(body.massa) || body.massa <= 0 || body.massa > 1e35) throw new Error('A massa deve ser positiva e no máximo 1e35 kg.');
        if (!Number.isFinite(body.densidade) || body.densidade <= 0 || body.densidade > 1e18) throw new Error('A densidade deve ser positiva e no máximo 1e18 kg/m³.');
        if ([body.posX, body.posY, body.velX, body.velY].some(value => !Number.isFinite(value) || Math.abs(value) > 1e20)) throw new Error('Posições e velocidades devem ser finitas, com módulo de até 1e20.');
        for (const field of ['x', 'y', 'vx', 'vy']) if (!$(`body-${field}`).value.trim()) throw new Error('Preencha todos os campos.');
        body.raio = Math.cbrt(3 * body.massa / body.densidade / (4 * Math.PI));
        if (!Number.isFinite(body.raio) || body.raio <= 0) throw new Error('A massa e a densidade produzem um raio fora do intervalo numérico. Ajuste esses valores.');
        if (editing === null) { state.corpos.push(body); selected = state.corpos.length - 1; }
        else { state.corpos[editing] = body; selected = editing; }
        commitEdit(); $('body-dialog').close(); toast('Corpo atualizado. As condições iniciais foram redefinidas.');
    } catch (error) { $('body-form-error').textContent = error.message; }
});

/* Observador de clique no botão de remoção de corpo, que remove o corpo selecionado do estado da simulação, 
atualiza a seleção e a interface, e exibe uma mensagem de confirmação. */
$('remove-body').addEventListener('click', () => {
    if (editing === null || state.corpos.length <= 1) return;
    state.corpos.splice(editing, 1); selected = Math.min(editing, state.corpos.length - 1);
    commitEdit(); $('body-dialog').close(); toast('Corpo removido.');
});

/* Função para salvar o estado atual da simulação no servidor, com confirmação do usuário se houver um estado existente, e exibir uma mensagem de sucesso ou erro. */
async function save() {
    if (!state || busy || transition) return;
    pause(); transition = true; setControls();
    try { readSettings(); const result = await api('/api/universes/save', state); toast(`Universo salvo em ${result.name}.`); }
    catch (error) { toast(error.message, true); }
    finally { transition = false; setControls(); }
}

/* Função para carregar a lista de universos salvos do servidor, exibindo-os em um diálogo com opções para abrir ou excluir cada arquivo, e atualizando a interface conforme necessário. */
async function loadLibrary() {
    try {
        const files = await api('/api/universes', undefined, 'GET');
        $('saved-list').replaceChildren();
        if (!files.length) { const p = document.createElement('p'); p.className = 'empty-state'; p.textContent = 'Ainda não há universos salvos. Salve seu primeiro experimento para encontrá-lo aqui.'; $('saved-list').append(p); }
/* Itera sobre cada arquivo salvo, criando elementos de interface para exibir o nome, data de modificação 
e botões para abrir, excluir ou iniciar o replay da simulação. */
        files.forEach(file => {
            const row = document.createElement('div'); row.className = 'saved-item';
            const content = document.createElement('div'); const name = document.createElement('strong'); name.textContent = file.name;
            const date = document.createElement('small'); date.textContent = new Date(file.modified).toLocaleString('pt-BR'); content.append(name, date);
            const open = document.createElement('button'); open.className = 'button button-outline'; open.textContent = 'Abrir';
/* Observador de clique no botão "Abrir", que solicita confirmação do usuário se houver um estado existente, substitui o 
estado atual da simulação pelo arquivo selecionado e exibe uma mensagem de sucesso ou erro. */
            open.addEventListener('click', async () => {
                if (busy || transition) return;
                $('library-dialog').close();
                if (state && !await confirmAction('Abrir universo salvo?', 'O universo atual será substituído pela simulação selecionada.')) return;
                transition = true; setControls();
                try { applyState(await api(`/api/universes/${file.id}`, undefined, 'GET'), file.name); toast('Universo carregado.'); }
                catch (error) { toast(error.message, true); }
                finally { transition = false; setControls(); }
            });

/* Observador de clique no botão "Excluir", que solicita confirmação do usuário antes de excluir o arquivo 
selecionado do servidor, atualiza a lista de arquivos e exibe uma mensagem de sucesso ou erro. */
            const remove = document.createElement('button');
            remove.className = 'icon-button';
            remove.textContent = 'Deletar simulação';
            remove.setAttribute('aria-label', `Excluir ${file.name}`);
            remove.addEventListener('click', async () => {
                $('library-dialog').close();
                if (await confirmAction('Excluir simulação?', `${file.name} será excluída do banco de dados. Esta ação não pode ser desfeita.`)) {
                    try { await api(`/api/universes/${file.id}`, undefined, 'DELETE'); toast('Simulação excluída.'); }
                    catch (error) { toast(error.message, true); }
                }
                await loadLibrary(); $('library-dialog').showModal();
            });

/* Observador de clique no botão "Replay", que solicita confirmação do usuário se houver um estado existente, 
inicia o replay da simulação selecionada e exibe uma mensagem de sucesso ou erro. */
            const replayButton = document.createElement('button');
            replayButton.className = 'button button-outline';
            replayButton.textContent = '▶ Replay';

            replayButton.addEventListener('click', async () => {
                if (busy || transition) return;
                $('library-dialog').close();
/* Solicita confirmação do usuário antes de iniciar o replay da simulação selecionada, informando que o estado atual será substituído. */
                if (state && !await confirmAction('Iniciar replay?', 'O universo atual será substituído pelo replay da simulação selecionada.')) return;

/* Define a variável de transição como verdadeira para indicar que uma operação de transição está em andamento, e atualiza 
os controles da interface para refletir esse estado. */
                transition = true;
                setControls();

/* Bloco try-catch-finally para abrir o replay da simulação selecionada, lidando com erros e atualizando a interface. */
                try { await openReplay(file); }
                catch (error) { toast(error.message, true); }
                finally { transition = false; setControls(); }
            });
/*  Adiciona os elementos de conteúdo e botões à linha da lista de arquivos salvos e, em seguida, adiciona a linha à lista na interface. */
            row.append(content, open, replayButton, remove);
            $('saved-list').append(row);

/* Catch de erro para lidar com falhas na solicitação de arquivos salvos, exibindo uma mensagem de erro na interface. */
        });
    } catch (error) { toast(error.message, true); }
}

/*  Função para ler as configurações de iterações e passo de tempo da interface, validá-las e aplicá-las ao estado da simulação. */
$('speed').addEventListener('input', () => { $('speed-label').textContent = `${$('speed').value}×`; });
for (const id of ['iterations', 'timestep']) $(id).addEventListener('change', () => {
    if (!state) return;
    try { readSettings(); initialState.quantidadeIteracoes = state.quantidadeIteracoes; initialState.tempoEntreIteracoes = state.tempoEntreIteracoes; updateUI(); }
    catch (error) { toast(error.message, true); }
});

/* Observadores de clique nos botões da interface, que chamam as funções correspondentes para gerar uma nova 
simulação, reproduzir a simulação, avançar um passo, redefinir a simulação, ajustar o zoom, abrir o diálogo de 
adição/edição de corpos, salvar, exportar, importar e abrir a biblioteca de universos salvos. */
$('generate-button').addEventListener('click', () => generate());
$('new-button').addEventListener('click', () => generate());
$('retry-button').addEventListener('click', () => generate(false));
$('play-button').addEventListener('click', play);
$('step-button').addEventListener('click', () => advance());
$('reset-button').addEventListener('click', reset);
$('zoom-in').addEventListener('click', () => { zoom = Math.min(100, zoom * 1.25); });
$('zoom-out').addEventListener('click', () => { zoom = Math.max(.03, zoom / 1.25); });
$('fit-button').addEventListener('click', fit);
$('add-body').addEventListener('click', () => openBody());
$('edit-body').addEventListener('click', () => openBody(selected));
$('save-button').addEventListener('click', save);
$('library-button').addEventListener('click', async () => { if (busy || transition) return; pause(); await loadLibrary(); $('library-dialog').showModal(); });
$('help-button').addEventListener('click', () => { pause(); $('help-dialog').showModal(); });

/* Observadores de eventos para fechar diálogos quando o usuário clica fora do 
conteúdo ou pressiona a tecla Escape, garantindo uma experiência de usuário consistente. */
document.querySelectorAll('.close-dialog').forEach(button => button.addEventListener('click', () => button.closest('dialog').close()));
document.querySelectorAll('dialog').forEach(dialog => dialog.addEventListener('click', event => { if (event.target === dialog) { const rect = dialog.getBoundingClientRect(); if (event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom) dialog.close(); } }));
document.addEventListener('keydown', event => {

    /*  Observador de eventos para capturar pressionamentos de teclas, permitindo controlar a 
simulação com atalhos de teclado, como pausar, avançar, ajustar o zoom e ajustar a visualização. */
    if (document.querySelector('dialog[open]') || /INPUT|SELECT|TEXTAREA|BUTTON/.test(document.activeElement.tagName)) return;
    if (event.code === 'Space') { event.preventDefault(); play(); }
    if (event.key === '+' || event.key === '=') zoom = Math.min(100, zoom * 1.25);
    if (event.key === '-') zoom = Math.max(.03, zoom / 1.25);
    if (event.key.toLowerCase() === 'f') fit();
});

/*  Função para pausar a simulação automaticamente quando a aba do navegador não estiver visível, evitando que a simulação continue em segundo plano e consuma recursos desnecessários. */
document.addEventListener('visibilitychange', () => { if (document.hidden) pause(); });
setControls();
draw();
generate(false);
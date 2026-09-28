// Per-chart plugin: redraws with Chart.js on resize, without global registrations or listeners.
const states = new WeakMap();
const font = '600 12px system-ui, sans-serif';
const labelHeight = 36;
const gap = 8;

function measure(ctx, label) {
    return Math.max(ctx.measureText(label.duration).width, ctx.measureText(label.percentage).width) + 12;
}

function drawLabel(ctx, label, x, y, width, theme) {
    ctx.fillStyle = theme === 'dark' ? '#17251fed' : '#ffffffed';
    ctx.beginPath();
    ctx.roundRect(x - width / 2, y - labelHeight / 2, width, labelHeight, 5);
    ctx.fill();
    ctx.fillStyle = theme === 'dark' ? '#e1ebe4' : '#223d32';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(label.duration, x, y - 8);
    ctx.fillText(label.percentage, x, y + 8);
}

function drawBars(chart, state) {
    const { ctx, chartArea } = chart;
    chart.getDatasetMeta(0).data.forEach((bar, index) => {
        const label = state.labels[index];
        if (!label) return;
        const { x, y, base } = bar.getProps(['x', 'y', 'base'], true);
        const width = measure(ctx, label);
        const target = x - base >= width + 12 ? x - width / 2 - 6 : base + width / 2 + 6;
        const center = Math.max(chartArea.left + width / 2, Math.min(chartArea.right - width / 2, target));
        drawLabel(ctx, label, center, y, width, state.theme);
    });
}

function drawPie(chart, state) {
    const { ctx } = chart;
    const outside = [[], []];
    const arcs = chart.getDatasetMeta(0).data;
    arcs.forEach((arc, index) => {
        const label = state.labels[index];
        if (!label || !(chart.data.datasets[0].data[index] > 0)) return;
        const { x, y, startAngle, endAngle, outerRadius } = arc.getProps(['x', 'y', 'startAngle', 'endAngle', 'outerRadius'], true);
        const angle = (startAngle + endAngle) / 2;
        const radius = outerRadius * .62;
        const width = measure(ctx, label);
        // Small slices use separated callouts; all labels remain visible without hovering.
        if (!state.callouts) {
            drawLabel(ctx, label, x + Math.cos(angle) * radius, y + Math.sin(angle) * radius, width, state.theme);
        } else {
            const right = Math.cos(angle) >= 0;
            outside[right ? 1 : 0].push({ label, width, right,
                x: x + Math.cos(angle) * outerRadius, y: y + Math.sin(angle) * outerRadius });
        }
    });
    for (const items of outside) {
        items.sort((a, b) => a.y - b.y);
        const minY = labelHeight / 2 + gap;
        const maxY = chart.height - minY;
        for (let i = 0; i < items.length; i++) {
            items[i].labelY = Math.max(items[i].y, i ? items[i - 1].labelY + labelHeight + gap : minY);
        }
        for (let i = items.length - 1; i >= 0; i--) {
            items[i].labelY = Math.min(items[i].labelY, i < items.length - 1 ? items[i + 1].labelY - labelHeight - gap : maxY);
        }
        for (const item of items) {
            const edge = item.right ? chart.width - gap : gap;
            const labelX = edge + (item.right ? -1 : 1) * item.width / 2;
            ctx.strokeStyle = state.theme === 'dark' ? '#b1beb5' : '#52635c';
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(item.x, item.y);
            ctx.lineTo(labelX + (item.right ? -1 : 1) * item.width / 2, item.labelY);
            ctx.stroke();
            drawLabel(ctx, item.label, labelX, item.labelY, item.width, state.theme);
        }
    }
}

const valueLabels = {
    id: 'ingacal-value-labels',
    beforeLayout(chart) {
        const state = states.get(chart);
        if (!state || chart.config.type !== 'pie') return;
        const data = chart.data.datasets[0].data;
        const total = data.reduce((sum, value) => sum + value, 0);
        state.callouts = data.length > 6 || data.some(value => value > 0 && value / total < .14);
        chart.ctx.save();
        chart.ctx.font = font;
        const padding = state.callouts ? Math.max(...state.labels.map(label => measure(chart.ctx, label)), 0) + 16 : 8;
        chart.ctx.restore();
        chart.options.layout.padding = { left: padding, right: padding, top: 8, bottom: 8 };
    },
    afterDatasetsDraw(chart) {
        const state = states.get(chart);
        if (!state) return;
        chart.ctx.save();
        chart.ctx.font = font;
        if (chart.config.type === 'bar') drawBars(chart, state);
        else drawPie(chart, state);
        chart.ctx.restore();
    },
    afterDestroy(chart) { states.delete(chart); }
};

function chartFor(region) {
    const canvas = region?.querySelector('canvas');
    return canvas ? Chart.getChart(canvas) : null;
}

export function updateLabels(barRegion, pieRegion, barLabels, pieLabels, theme) {
    for (const [region, labels] of [[barRegion, barLabels], [pieRegion, pieLabels]]) {
        const chart = chartFor(region);
        if (!chart) continue;
        states.set(chart, { labels, theme, callouts: false });
        if (!chart.config.plugins.includes(valueLabels)) chart.config.plugins.push(valueLabels);
        chart.update('none');
    }
}

export function disposeLabels(barRegion, pieRegion) {
    for (const region of [barRegion, pieRegion]) {
        const chart = chartFor(region);
        if (!chart) continue;
        states.delete(chart);
        const index = chart.config.plugins.indexOf(valueLabels);
        if (index >= 0) chart.config.plugins.splice(index, 1);
    }
}

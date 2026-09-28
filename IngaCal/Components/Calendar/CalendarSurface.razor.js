const RANGE = "OnRange", SELECT = "OnSelect", ACTIVITY = "OnActivity", CHANGE = "OnChange";
const instances = new WeakMap();
function calendarEvents(events) {
    return events.map(event => {
        const rgb = event.color.slice(1).match(/../g).map(x => parseInt(x, 16) / 255);
        const linear = rgb.map(x => x <= .04045 ? x / 12.92 : ((x + .055) / 1.055) ** 2.4);
        const luminance = .2126 * linear[0] + .7152 * linear[1] + .0722 * linear[2];
        return { ...event, contrastColor: luminance > .179 ? "#111111" : "#ffffff" };
    });
}
class CalendarBridge {
    #reference; #calendar; #state; #observer; #disposed = false;
    constructor(element, reference, state) {
        this.#reference = reference;
        this.#state = state;
        this.#calendar = new FullCalendar.Calendar(element, {
            initialDate: state.date, initialView: state.view, timeZone: state.timeZone,
            headerToolbar: false, height: 650, firstDay: 1, nowIndicator: true,
            allDaySlot: false, slotDuration: "00:15:00", snapDuration: "00:15:00",
            slotHeaderInterval: "01:00:00", scrollTime: "07:00:00",
            editable: true, eventResizableFromStart: true, selectable: true,
            selectMirror: true,
            // The server validates overlaps and versions, so failed pointer edits can explain and revert.
            eventOverlap: true, selectOverlap: true,
            dayMaxEvents: 3, eventMinHeight: 22, events: calendarEvents(state.events),
            eventTimeFormat: { hour: "numeric", minute: "2-digit", meridiem: "short" },
            datesSet: info => this.send(RANGE, { start: info.start.toISOString(), end: info.end.toISOString() }),
            select: info => {
                this.#calendar.unselect();
                if (info.allDay) {
                    // A month selection opens an editable timed draft.
                    this.send(SELECT, { start: info.start.toISOString(), end: new Date(info.start.getTime() + 30 * 60000).toISOString() });
                } else this.send(SELECT, { start: info.start.toISOString(), end: info.end.toISOString() });
            },
            eventClick: info => this.send(ACTIVITY, info.event.id),
            eventDrop: info => this.commit(info, true),
            eventResize: info => this.commit(info),
            eventDidMount: info => {
                info.el.title = [info.event.title, info.event.extendedProps.description, info.event.extendedProps.tags].filter(Boolean).join("\n");
                info.el.setAttribute("tabindex", "0");
                info.el.setAttribute("role", "button");
                info.el.setAttribute("aria-label", "Edit " + info.event.title);
                info.el.addEventListener("keydown", event => {
                    if (event.key === "Enter" || event.key === " ") { event.preventDefault(); this.send(ACTIVITY, info.event.id); }
                });
            }
        });
        this.#calendar.render();
        this.#observer = new MutationObserver(() => { if (!element.isConnected) this.dispose(); });
        this.#observer.observe(document.body, { childList: true, subtree: true });
    }
    async send(method, value) { try { return await this.#reference.invokeMethodAsync(method, value); } catch { return false; } }
    async commit(info, moving = false) {
        this.#calendar.setOption("editable", false);
        try {
            const end = moving ? new Date(info.event.start.getTime() + info.oldEvent.end.getTime() - info.oldEvent.start.getTime()) : info.event.end;
            const ok = await this.send(CHANGE, { id: info.event.id, start: info.event.start.toISOString(), end: end.toISOString() });
            if (!ok) info.revert();
        } finally { this.#calendar.setOption("editable", true); }
    }
    update(state) {
        const before = this.#state; this.#state = state;
        this.#calendar.batchRendering(() => {
            if (before.timeZone !== state.timeZone) this.#calendar.setOption("timeZone", state.timeZone);
            if (before.view !== state.view) this.#calendar.changeView(state.view, state.date);
            else if (before.date !== state.date) this.#calendar.gotoDate(state.date);
            this.#calendar.removeAllEventSources();
            this.#calendar.addEventSource(calendarEvents(state.events));
        });
    }
    dispose() { if (this.#disposed) return; this.#disposed = true; this.#observer?.disconnect(); this.#calendar.destroy(); }
}
export function initialize(element, reference, state) { instances.get(element)?.dispose(); instances.set(element, new CalendarBridge(element, reference, state)); }
export function update(element, state) { instances.get(element)?.update(state); }
export function dispose(element) { instances.get(element)?.dispose(); instances.delete(element); }

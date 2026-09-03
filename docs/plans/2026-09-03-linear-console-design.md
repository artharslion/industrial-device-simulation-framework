# Linear-Inspired Developer Console Design

## Intent

The v0.1 Web UI remains an operational developer console, not a factory
management product. The redesign adopts Linear's restrained desktop-product
qualities—high information density, quiet surfaces, precise borders, and one
clear accent color—without copying its brand. The console is implemented as a
Vue 3, Vite, and TypeScript application served by the existing ASP.NET Core
host.

## Structure

- A persistent workspace sidebar provides stable destinations for overview,
  state, events, scenarios, and faults.
- A sticky context header reports the current workspace and refresh status.
- Status cards summarize runtime, OPC UA, Modbus TCP, and active work.
- A 12-column dashboard keeps StateStore and events visually primary while
  lifecycle, scenario, and fault controls remain close at hand.
- Below tablet width the sidebar becomes a horizontal navigation strip and
  panels collapse into a single-column operational flow.

## Visual System

CSS custom properties define canvas, surfaces, borders, text hierarchy,
status colors, accent color, radius, and shadow. Components reuse the same
panel, button, form, badge, and status rules so later runtime screens can be
added without inventing new styling conventions. The palette is deliberately
dark and low-contrast, with violet reserved for selection and primary action.

## Interaction and Safety

Existing API routes remain unchanged. Runtime data renders through Vue text
bindings; no API-provided content is inserted as raw HTML. Focus-visible styles,
live regions, disabled states, reduced-motion support, and responsive
breakpoints are part of the base design system. Native semantic controls and
small local components are preferred over a general UI kit so the console
retains its visual identity and a small production bundle.

## Verification

- Web contract tests assert that the operational controls, safe DOM rendering,
  scalable workspace landmarks, and live status regions remain present.
- Desktop and narrow-screen browser checks cover hierarchy, overflow, and
  runtime data rendering against a live local host.

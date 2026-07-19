# PCG Scene Asset (PcgComponent)

The scene component that hosts a generation graph on a GameObject and bridges it to the scene. It owns the graph data, exposes its variables for editing, and routes generated instances to the [[Instancers|Instancers]] on the same GameObject.

## How it works

1. Add the component to a GameObject in your scene.
2. Press **Open Graph** to edit its graph in the node editor.
3. Add Instancer components so the graph has somewhere to spawn objects.
4. Build a pipeline that ends in a [[Result Node|PCG/Result-Node]].
5. Press **Generate** (in the graph toolbar) to materialize objects into the scene.

Each component holds its own graph and its own generated objects. Generation is baked into the scene, so the result ships in builds without the editor pipeline.

## Fields

### Open Graph

Opens the node editor bound to this component's graph.

### Sub Graph

Optional reference to a reusable [[PCG Asset|PCG/Pcg-Asset]] (`PcgSubGraph`). When set, the component is driven by that sub graph instead of a hand-made graph — see [Sub Graph mode](#sub-graph-mode).

### Instancer Components

The Instancers that receive generated instances. Auto-filled from Instancer components on the same GameObject; each handles its own instance type. See [[Instancers|Instancers]].

### Auto Generate

When enabled, the component regenerates automatically in the editor whenever the graph result changes. It does not run in Play Mode or builds.

### Variables

Values for the graph's blackboard variables, edited on the component like material properties on a shader. Values are stored per-component and mirror the graph's variable definitions by id, so renaming a variable in the graph keeps its value.

## Sub Graph mode

Assigning a **Sub Graph** asset turns the component into a "material" for that sub graph, the same way a material drives a shader. The component's graph is replaced by an auto-built **wrapper** and closed for manual editing:

* The sub graph's **variables** and **inputs** become editable values in the **Variables** section of the inspector.
* The sub graph's instance **outputs** flow straight into the [[Result Node|PCG/Result-Node]], so **Generate** / **Clear** work as usual.
* **Open Graph** opens directly inside the sub graph's content, in the context of this component. The root breadcrumb is a plain label — you edit the shared sub graph asset here.

The wrapper re-syncs itself automatically when the sub graph's interface changes (a variable, input, or output is added, removed, or renamed). Surviving values are kept; existing generated objects are not disturbed.

### Assigning, replacing and clearing

* **Empty component → assign an asset:** the wrapper is built and its parameters appear in the inspector.
* **Hand-made graph → assign an asset:** a confirmation dialog appears, because the existing graph is replaced by the wrapper.
* **Replace asset A → B:** objects from A are removed and the wrapper for B is built, without a dialog.
* **Set the field to None:** generated objects are removed and the component becomes an ordinary, empty graph again.

### Convert to Editable Graph

Drops the sub graph reference while keeping the current wrapper graph as-is. The graph becomes a normal, editable graph — objects, caches and values are left untouched. Use this when you want to start from the sub graph and then customize it.

### Missing asset

If the referenced sub graph asset is deleted from the project, nothing is auto-cleared: the graph window shows a "Sub graph asset is missing" warning and the inspector shows a warning. Clear the reference explicitly (set it to None) to reset the component.

## See also

* [[Nodes|Nodes]] - node types and how they work
* [[Result Node|PCG/Result-Node]] - final node that outputs instances to the scene
* [[Instancers|Instancers]] - creating objects in the scene
* [[Variable Node|PCG/Variables/Variable-Node]] - reading blackboard variables in a graph

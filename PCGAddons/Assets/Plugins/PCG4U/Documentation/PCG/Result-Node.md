# ResultNode

Final node that applies spawned instances to an injected instancer container.
Supports async Generate and Clear operations with cancellation handling.

## Inputs

### Instances

Input list(s) of instances produced upstream.

## Properties

### ObjectsCount

Number of objects currently held by the instancer container.

### Processing

Indicates whether a generate/clear operation is currently running.

## Methods

### Generate

Starts asynchronous generation: pushes input instances into the instancer container.


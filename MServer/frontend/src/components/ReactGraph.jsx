import React, { useRef, useState, forwardRef, useImperativeHandle } from "react";
import chroma from "chroma-js";
import { v4 as uuid } from "uuid";

// 1. Add parallel property to initial nodes and new nodes
const initialNodes = [
  { id: "n1", x: 200, y: 100, size: 30, color: chroma.random().hex(), highlighted: false, label: "node1", script: { language: "python", filename: "Node1.py", code: "" }, parallel: true },
  { id: "n2", x: 100, y: 200, size: 30, color: chroma.random().hex(), highlighted: false, label: "node2", script: { language: "python", filename: "Node2.py", code: "" }, parallel: false },
  { id: "n3", x: 300, y: 250, size: 30, color: chroma.random().hex(), highlighted: false, label: "node3", script: { language: "python", filename: "Node3.py", code: "" }, parallel: false },
  { id: "n4", x: 400, y: 180, size: 30, color: chroma.random().hex(), highlighted: false, label: "node4", script: { language: "python", filename: "Node4.py", code: "" }, parallel: false },
];

const initialEdges = [
  { source: "n1", target: "n2" },
  { source: "n2", target: "n3" },
  { source: "n1", target: "n4" },
];

const ReactGraph = forwardRef(({ onNodeSelect }, ref) => {
  const [nodes, setNodes] = useState(initialNodes);
  const [edges, setEdges] = useState(initialEdges);
  const [draggedNodeId, setDraggedNodeId] = useState(null);
  const [selectedNodeId, setSelectedNodeId] = useState(null);
  const [contextMenu, setContextMenu] = useState(null);
  const [editingLabelNodeId, setEditingLabelNodeId] = useState(null);
  const [labelInput, setLabelInput] = useState("");
  const svgRef = useRef(null);
  const [isPlaying, setIsPlaying] = useState(false);
  const [currentStep, setCurrentStep] = useState(0);
  const [scale, setScale] = useState(1);

  
React.useEffect(() => {
  const svg = svgRef.current;
  if (!svg) return;
  const wheelHandler = (evt) => {
    evt.preventDefault();
    // Your zoom logic here, e.g.:
    setScale(prev => {
      let next = prev - evt.deltaY * 0.001;
      next = Math.max(0.2, Math.min(3, next));
      return next;
    });
  };
  svg.addEventListener("wheel", wheelHandler, { passive: false });
  return () => svg.removeEventListener("wheel", wheelHandler);
}, [svgRef]);
  // Hide menu on click elsewhere
React.useEffect(() => {
  const hideMenu = () => setContextMenu(null);
  if (contextMenu) {
    window.addEventListener("mousedown", hideMenu);
    return () => window.removeEventListener("mousedown", hideMenu);
  }
}, [contextMenu]);

// Animation effect for stepping through the execution order
React.useEffect(() => {
  if (!isPlaying) return;
  const order = getOrderedNodeIds(nodes, edges);
  if (currentStep >= order.length) {
    setIsPlaying(false);
    setNodes(prev => prev.map(n => ({ ...n, highlighted: false })));
    return;
  }
  setNodes(prev =>
    prev.map(n =>
      n.id === order[currentStep]
        ? { ...n, highlighted: true }
        : { ...n, highlighted: false }
    )
  );
  const timer = setTimeout(() => setCurrentStep(s => s + 1), 800);
  return () => clearTimeout(timer);
}, [isPlaying, currentStep, edges]); // <-- REMOVE nodes here

  // Play handler
  const handlePlay = () => {
    setIsPlaying(true);
    setCurrentStep(0);
  };


  // Expose updateNodeScriptCode to parent
  useImperativeHandle(ref, () => ({
    updateNodeScriptCode: (nodeId, code) => {
      setNodes(prev =>
        prev.map(n =>
          n.id === nodeId
            ? { ...n, script: { ...n.script, code } }
            : n
        )
      );
    }
  }));

  // Helper to get mouse position relative to SVG
const getSvgCoords = (evt) => {
  const svg = svgRef.current;
  const pt = svg.createSVGPoint();
  pt.x = evt.clientX;
  pt.y = evt.clientY;
  const cursorpt = pt.matrixTransform(svg.getScreenCTM().inverse());
  // Adjust for zoom
  return { x: cursorpt.x / scale, y: cursorpt.y / scale };
};

  // Drag logic
  const handleMouseDown = (evt, nodeId) => {
    if (evt.button !== 0) return;
    setDraggedNodeId(nodeId);
  };
  const handleMouseMove = (evt) => {
    if (!draggedNodeId) return;
    const { x, y } = getSvgCoords(evt);
    setNodes((prev) =>
      prev.map((n) =>
        n.id === draggedNodeId ? { ...n, x, y } : n
      )
    );
  };
  const handleMouseUp = () => setDraggedNodeId(null);

// In handleDoubleClick, add parallel: false to new nodes
const handleDoubleClick = (evt) => {
  const { x, y } = getSvgCoords(evt);
  const newNodeIndex = nodes.length + 1;
  const defaultLabel = `Node_${newNodeIndex}`;
  setNodes((prev) => [
    ...prev,
    {
      id: uuid(),
      x,
      y,
      size: 30,
      color: chroma.random().hex(),
      highlighted: false,
      label: defaultLabel,
      script: { language: "python", filename: `${defaultLabel}.py`, code: "" },
      parallel: false,
    },
  ]);
};

  // Right click to connect or show menu (no delete on right click)
  const handleRightClick = (evt, nodeId) => {
    evt.preventDefault();
    if (selectedNodeId && selectedNodeId !== nodeId) {
      setEdges((prev) => [
        ...prev,
        { source: selectedNodeId, target: nodeId }
      ]);
      setSelectedNodeId(null);
      setNodes((prev) =>
        prev.map((n) =>
          n.id === selectedNodeId ? { ...n, highlighted: false } : n
        )
      );
      if (onNodeSelect) onNodeSelect(null);
    } else if (selectedNodeId === nodeId) {
      setContextMenu({
        x: evt.clientX,
        y: evt.clientY,
        nodeId,
      });
    }
    // No delete logic here
  };

  // Click to select node (highlight orange)
const handleClick = (evt, nodeId) => {
  setSelectedNodeId((prevSelected) => {
    if (prevSelected === nodeId) {
      setNodes((prev) =>
        prev.map((n) =>
          n.id === nodeId ? { ...n, highlighted: false } : n
        )
      );
      if (onNodeSelect) setTimeout(() => onNodeSelect(null), 0);
      return null;
    } else {
      const node = nodes.find(n => n.id === nodeId);
      setNodes((prev) =>
        prev.map((n) =>
          n.id === nodeId
            ? { ...n, highlighted: true }
            : { ...n, highlighted: false }
        )
      );
      if (onNodeSelect && prevSelected !== nodeId) setTimeout(() => onNodeSelect(node), 0);
      return nodeId;
    }
  });
  setContextMenu(null);
};
  // Handle context menu actions
  const handleMenuAction = (action) => {
    if (!contextMenu) return;
    if (action === "delete") {
      setNodes((prev) => prev.filter((n) => n.id !== contextMenu.nodeId));
      setEdges((prev) => prev.filter((e) => e.source !== contextMenu.nodeId && e.target !== contextMenu.nodeId));
      if (selectedNodeId === contextMenu.nodeId) setSelectedNodeId(null);
      if (onNodeSelect) onNodeSelect(null);
    }
    if (action === "setName") {
      setEditingLabelNodeId(contextMenu.nodeId);
      const node = nodes.find(n => n.id === contextMenu.nodeId);
      setLabelInput(node?.label || "");
    }
    setContextMenu(null);
  };

  // Handle label input submit (from menu)
  const handleLabelInputKeyDown = (e) => {
    if (e.key === "Enter" && editingLabelNodeId) {
      const safeLabel = labelInput.replace(/\s+/g, "_");
      setNodes((prev) =>
        prev.map((n) =>
          n.id === editingLabelNodeId
            ? {
                ...n,
                label: labelInput,
                script: { ...n.script, filename: `${safeLabel}.py` }
              }
            : n
        )
      );
      setEditingLabelNodeId(null);
      setLabelInput("");
    }
  };

  // Find the node being edited for label
  const editingNode = editingLabelNodeId
    ? nodes.find(n => n.id === editingLabelNodeId)
    : null;

  // Find the selected node for property panel
  const selectedNode = selectedNodeId
    ? nodes.find(n => n.id === selectedNodeId)
    : null;

  // Handle property update
  const handlePropChange = (key, value) => {
    setNodes(prev =>
      prev.map(n =>
        n.id === selectedNodeId
          ? { ...n, [key]: value }
          : n
      )
    );
    // If label changes, update script.filename as well (replace spaces with "_")
    if (key === "label") {
      const safeLabel = value.replace(/\s+/g, "_");
      setNodes(prev =>
        prev.map(n =>
          n.id === selectedNodeId
            ? { ...n, script: { ...n.script, filename: `${safeLabel}.py` } }
            : n
        )
      );
    }
    // If script.filename changes, update label to match (remove .py and replace _ with space)
    if (key === "script.filename") {
      const labelFromFilename = value.replace(/\.py$/, "").replace(/_/g, " ");
      setNodes(prev =>
        prev.map(n =>
          n.id === selectedNodeId
            ? { ...n, label: labelFromFilename }
            : n
        )
      );
    }
  };

  // Handle script property update (except code)
  const handleScriptChange = (subkey, value) => {
    setNodes(prev =>
      prev.map(n =>
        n.id === selectedNodeId
          ? { ...n, script: { ...n.script, [subkey]: value } }
          : n
      )
    );
  };

function getOrderedNodeIds(nodes, edges) {
  const targets = new Set(edges.map(e => e.target));
  const startNode = nodes.find(n => !targets.has(n.id));
  if (!startNode) return [];
  const order = [];
  const visited = new Set();

  function dfs(nodeId) {
    if (visited.has(nodeId)) return;
    visited.add(nodeId);
    order.push(nodeId);
    // Find all outgoing edges from this node
    edges
      .filter(e => e.source === nodeId)
      .forEach(e => dfs(e.target));
  }

  dfs(startNode.id);
  return order;
}

return (
  <div style={{ width: "100%", height: "100%", position: "relative", display: "flex", flexDirection: "column" }}>
    <div style={{ flex: 1, position: "relative", display: "flex" }}>
      <div style={{ flex: 1, position: "relative" }}>
        <svg
  ref={svgRef}
  width="100%"
  height="100%"
  style={{ background: "#222", width: "100%", height: "100%", touchAction: "none" }}
  onMouseMove={handleMouseMove}
  onMouseUp={handleMouseUp}
  onDoubleClick={handleDoubleClick}

>
  <defs>
    <marker
      id="arrow"
      markerWidth="16"
      markerHeight="16"
      refX="14"
      refY="8"
      orient="auto"
      markerUnits="userSpaceOnUse"
    >
      <polygon points="2,2 14,8 2,14" fill="#fff" />
    </marker>
  </defs>
  <g transform={`scale(${scale})`}>
    {/* Edges */}
    {edges.map((e, i) => {
      const source = nodes.find((n) => n.id === e.source);
      const target = nodes.find((n) => n.id === e.target);
      if (!source || !target) return null;
      return (
        <line
          key={i}
          x1={source.x}
          y1={source.y}
          x2={target.x}
          y2={target.y}
          stroke="#fff"
          strokeWidth={2}
          opacity={0.7}
          markerEnd="url(#arrow)"
        />
      );
    })}
    {/* Nodes */}
    {nodes.map((n) => (
      <g
        key={n.id}
        onMouseDown={(evt) => handleMouseDown(evt, n.id)}
        onClick={(evt) => handleClick(evt, n.id)}
        onContextMenu={(evt) => handleRightClick(evt, n.id)}
        style={{ cursor: "pointer" }}
      >
        <circle
          cx={n.x}
          cy={n.y}
          r={n.size}
          fill={n.highlighted ? "#ff9800" : n.color}
          stroke="#fff"
          strokeWidth={n.highlighted ? 4 : 2}
        />
        {n.label && (
          <text
            x={n.x}
            y={n.y + n.size + 18}
            textAnchor="middle"
            alignmentBaseline="hanging"
            fill="#fff"
            fontSize={16}
            pointerEvents="none"
          >
            {n.label}
          </text>
        )}
      </g>
    ))}
  </g>
</svg>
        {/* Render input for label editing */}
        {editingNode && (
          <input
            type="text"
            value={labelInput}
            autoFocus
            style={{
              position: "absolute",
              left: editingNode.x - 50,
              top: editingNode.y + editingNode.size + 30,
              width: 100,
              zIndex: 20,
              background: "#222",
              color: "#fff",
              border: "1px solid #444",
              borderRadius: 4,
              padding: 4,
              outline: "none",
              textAlign: "center",
            }}
            onChange={e => setLabelInput(e.target.value)}
            onKeyDown={handleLabelInputKeyDown}
            onBlur={() => setEditingLabelNodeId(null)}
          />
        )}
        {contextMenu && (
          <div
            style={{
              position: "absolute",
              top: contextMenu.y,
              left: contextMenu.x,
              background: "#222",
              color: "#fff",
              border: "1px solid #444",
              borderRadius: 4,
              zIndex: 10,
              minWidth: 100,
              boxShadow: "0 2px 8px rgba(0,0,0,0.3)",
              padding: 4,
            }}
            onMouseDown={e => e.stopPropagation()}
          >
            <div style={{ padding: 8, cursor: "pointer" }} onClick={() => handleMenuAction("execute")}>Execute</div>
            <div style={{ padding: 8, cursor: "pointer" }} onClick={() => handleMenuAction("setName")}>Set name</div>
            <div style={{ padding: 8, cursor: "pointer", color: "#ff5555" }} onClick={() => handleMenuAction("delete")}>Delete</div>
          </div>
        )}
      </div>
      {/* Properties panel */}
      {selectedNode && (
        <div
          style={{
            width: 320,
            background: "#23272e",
            color: "#fff",
            padding: 16,
            borderLeft: "1px solid #333",
            overflowY: "auto",
            height: "100%",
          }}
        >
          <details open>
            <summary style={{ fontWeight: "bold", fontSize: 18, marginBottom: 8 }}>
              Node Properties
            </summary>
<ul style={{ 
  listStyle: "none", 
  padding: 0, 
  display: "grid", 
  gridTemplateColumns: "max-content 1fr", 
  rowGap: 12, 
  columnGap: 12 
}}>
  {Object.entries(selectedNode).map(([key, value]) =>
    key !== "script" && key !== "parallel" ? (
      <React.Fragment key={key}>
        <li style={{ fontWeight: "bold", gridColumn: 1 }}>{key}:</li>
        <li style={{ gridColumn: 2 }}>
          <input
            style={{
              width: "95%",
              background: "#181a1b",
              color: "#fff",
              border: "1px solid #444",
              borderRadius: 4,
              padding: "2px 6px"
            }}
            value={value}
            onChange={e => handlePropChange(key, e.target.value)}
            disabled={key === "id"}
          />
        </li>
      </React.Fragment>
    ) : key === "parallel" ? (
      <React.Fragment key={key}>
        <li style={{ fontWeight: "bold", gridColumn: 1 }}>{key}:</li>
        <li style={{ gridColumn: 2 }}>
          <input
            style={{
              width: "95%",
              background: "#181a1b",
              color: "#fff",
              border: "1px solid #444",
              borderRadius: 4,
              padding: "2px 6px"
            }}
            value={String(value)}
            onChange={e => handlePropChange(key, e.target.value === "true")}
          />
        </li>
      </React.Fragment>
    ) : (
      <React.Fragment key={key}>
        <li style={{ fontWeight: "bold", gridColumn: 1 }}>script.language:</li>
        <li style={{ gridColumn: 2 }}>
          <input
            style={{
              width: "95%",
              background: "#181a1b",
              color: "#fff",
              border: "1px solid #444",
              borderRadius: 4,
              padding: "2px 6px"
            }}
            value={value.language}
            onChange={e => handleScriptChange("language", e.target.value)}
          />
        </li>
        <li style={{ fontWeight: "bold", gridColumn: 1 }}>script.filename:</li>
        <li style={{ gridColumn: 2 }}>
          <input
            style={{
              width: "95%",
              background: "#181a1b",
              color: "#fff",
              border: "1px solid #444",
              borderRadius: 4,
              padding: "2px 6px"
            }}
            value={value.filename}
            onChange={e => handlePropChange("script.filename", e.target.value)}
          />
        </li>
      </React.Fragment>
    )
  )}
</ul>
          </details>
        </div>
      )}
    </div>
    {/* Bottom nav bar with play, pause, stop icons */}
    <nav
      style={{
        width: "100%",
        height: 60,
        background: "#23272e",
        borderTop: "1px solid #333",
        display: "flex",
        justifyContent: "center",
        alignItems: "center",
        position: "relative",
        zIndex: 5,
      }}
    >
      <button
        style={{
          background: "none",
          border: "none",
          color: "#fff",
          fontSize: 32,
          margin: "0 32px",
          cursor: "pointer"
        }}
        title="Play"
        onClick={handlePlay}
      >
        {/* Play Icon */}
        <svg width="32" height="32" viewBox="0 0 32 32" fill="none">
          <circle cx="16" cy="16" r="16" fill="#222" />
          <polygon points="12,9 25,16 12,23" fill="#4caf50" />
        </svg>
      </button>
      <button
        style={{
          background: "none",
          border: "none",
          color: "#fff",
          fontSize: 32,
          margin: "0 32px",
          cursor: "pointer"
        }}
        title="Pause"
        // onClick={handlePause} // implement as needed
      >
        {/* Pause Icon */}
        <svg width="32" height="32" viewBox="0 0 32 32" fill="none">
          <circle cx="16" cy="16" r="16" fill="#222" />
          <rect x="11" y="10" width="4" height="12" fill="#ffb300" />
          <rect x="17" y="10" width="4" height="12" fill="#ffb300" />
        </svg>
      </button>
      <button
        style={{
          background: "none",
          border: "none",
          color: "#fff",
          fontSize: 32,
          margin: "0 32px",
          cursor: "pointer"
        }}
        title="Stop"
        // onClick={handleStop} // implement as needed
      >
        {/* Stop Icon */}
        <svg width="32" height="32" viewBox="0 0 32 32" fill="none">
          <circle cx="16" cy="16" r="16" fill="#222" />
          <rect x="11" y="11" width="10" height="10" fill="#f44336" />
        </svg>
      </button>
    </nav>
  </div>
);
});

export default ReactGraph;
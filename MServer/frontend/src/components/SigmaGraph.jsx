import React, { useEffect, useRef, useState } from "react";
import chroma from "chroma-js";
import Graph from "graphology";
import Sigma from "sigma";
import { v4 as uuid } from "uuid";

export default function SigmaGraph({ resizeSignal }) {
    const containerRef = useRef(null);
    const rendererRef = useRef(null);
    const [selectedNode, setSelectedNode] = useState(null);

    useEffect(() => {
        if (!containerRef.current) return;

        const graph = new Graph({ multi: true });
        graph.addNode("n1", { x: 0, y: 0, size: 30, color: chroma.random().hex() });
        graph.addNode("n2", { x: -5, y: 5, size: 30, color: chroma.random().hex() });
        graph.addNode("n3", { x: -10, y: 10, size: 30, color: chroma.random().hex() });
        graph.addEdge("n1", "n2");
        graph.addEdge("n2", "n3");

        const renderer = new Sigma(graph, containerRef.current, {
            enableEdgeHovering: true,
            edgeHoverSizeRatio: 5,
            edgeColor: 'white',
            allowInvalidContainer: true,
        });

        rendererRef.current = renderer;

        // --- Drag node logic ---
        let draggedNode = null;
        let isDragging = false;

        renderer.on("downNode", (e) => {
            if (e.event.original.button !== 0) return;
            isDragging = true;
            draggedNode = e.node;
            graph.setNodeAttribute(draggedNode, "highlighted", true);
            if (!renderer.getCustomBBox()) renderer.setCustomBBox(renderer.getBBox());
        });

        const handleUp = (e) => {
            isDragging = false;
            draggedNode = null;
        };

        renderer.on("upNode", handleUp);
        renderer.on("upStage", handleUp);

        renderer.on("moveBody", ({ event }) => {
            if (!isDragging || !draggedNode) return;
            if (event.original.button !== 0) return; // Only left button
            const pos = renderer.viewportToGraph(event);
            graph.setNodeAttribute(draggedNode, "x", pos.x);
            graph.setNodeAttribute(draggedNode, "y", pos.y);
            event.preventSigmaDefault();
        });

        // --- Double click on canvas to create a node ---
        renderer.on("doubleClickStage", ({ event }) => {
            const coordForGraph = renderer.viewportToGraph({ x: event.x, y: event.y });
            const id = uuid();
            graph.addNode(id, {
                ...coordForGraph,
                size: 0,
                color: chroma.random().hex(),
            });
        });

        // Right click node
        renderer.on("rightClickNode", ({ node }) => {
            if (graph.hasNode(node)) {
                graph.dropNode(node);
            }
        });

        // Left click node
        renderer.on("clickNode", ({ node }) => {
            const highlightedNodes = [];
            graph.forEachNode((n) => {
                if (graph.getNodeAttribute(n, "highlighted")) {
                    highlightedNodes.push(n);
                }
            });
            console.log("Highlighted nodes:", highlightedNodes);
            //add edge between the highlighted nodes in array
            if (highlightedNodes.length > 1) {
                highlightedNodes.forEach((n, index) => {
                    if (index < highlightedNodes.length - 1) {
                        graph.addEdge(n, highlightedNodes[index + 1]);
                    }
                });
                //remove all highlighted nodes
                highlightedNodes.forEach((n) => {
                    graph.setNodeAttribute(n, "highlighted", false);
                });
            }
        });

        return () => {
            renderer.kill();
        };
    }, [selectedNode]);

    // Refresh Sigma renderer on resizeSignal change
    useEffect(() => {
        if (rendererRef.current) {
            rendererRef.current.refresh();
        }
    }, [resizeSignal]);

    return (
        <div
            id="sigma-container"
            ref={containerRef}
            style={{ width: "100%", height: "100%",  }}
        />
    );
}
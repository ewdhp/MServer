import React, { useState, useRef } from 'react';
import { Editor } from '@monaco-editor/react';
import Terminal from '../components/Terminal';
import ReactGraph from '../components/ReactGraph';

const paneStyle = {
  height: '100%',
  width: '100%',
  overflow: 'hidden',
  display: 'flex'
};
const leftPaneStyle = {
  flex: .57,
  display: 'flex',
  flexDirection: 'column',
  minWidth: 0,
  borderRight: '1px solid #ccc'
};
const rightPaneStyle = {
  flex: .43,
  minWidth: 0,
  display: 'flex',
  flexDirection: 'column'
};
const topLeftStyle = {
  flex: .65,
  minHeight: 0,
  borderBottom: '1px solid #ccc'
};
const bottomLeftStyle = {
  flex: .35,
  minHeight: 0
};

const Dashboard = () => {
  const [selectedNode, setSelectedNode] = useState(null);
  const [code, setCode] = useState('');
  const graphRef = useRef();

  // When a node is selected in ReactGraph, update selectedNode and code
  const handleNodeSelect = (node) => {
    setSelectedNode(node);
    setCode(node?.script?.code || '');
  };

  // When code is changed in the editor, update the node's script.code
  const handleEditorChange = (value) => {
    setCode(value);
    if (selectedNode) {
      graphRef.current?.updateNodeScriptCode(selectedNode.id, value);
    }
  };

  return (
    <div style={{ width: '100vw', height: '100vh' }}>
      <div style={paneStyle}>
        {/* Left Pane */}
        <div style={leftPaneStyle}>
          <div style={topLeftStyle}>
            <ReactGraph
              ref={graphRef}
              onNodeSelect={handleNodeSelect}
            />
          </div>
          <div style={bottomLeftStyle}>
            <Terminal/>  
          </div>
        </div>
        {/* Right Pane */}
        <div style={rightPaneStyle}>
          <Editor
            height="100%"
            width="100%"
            value={code}
            onChange={handleEditorChange}
            language={selectedNode?.script?.language || "python"}
          />
        </div>
      </div>
    </div>
  );
};

export default Dashboard;
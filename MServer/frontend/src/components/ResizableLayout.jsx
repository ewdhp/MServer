import SplitPane from 'react-split-pane';

// Inline CSS for SplitPane divider and pane resizing
const splitPaneStyles = `
  .SplitPane {
    position: relative !important;
    height: 100% !important;
  }
  .Resizer {
    background: #ccc;
    opacity: 0.8;
    z-index: 1;
    box-sizing: border-box;
    background-clip: padding-box;
  }
  .Resizer.vertical {
    width: 8px;
    margin: 0 -4px;
    cursor: col-resize;
    border-left: 1px solid #eee;
    border-right: 1px solid #eee;
  }
  .Resizer.horizontal {
    height: 8px;
    margin: -4px 0;
    cursor: row-resize;
    border-top: 1px solid #eee;
    border-bottom: 1px solid #eee;
  }
  .Resizer:hover {
    transition: all 0.2s ease;
    background: #aaa;
  }
`;

const ResizableLayoutMui = ({
  leftComponent,
  leftBottomComponent,
  rightComponent,
  onLeftPaneResize,
  onLeftBottomPaneResize,
}) => (
  <>
    <style>{splitPaneStyles}</style>
    <SplitPane
      split="vertical"
      minSize={100}
      defaultSize="50%"
      style={{ height: '100%' }}
      onChange={onLeftPaneResize}
    >
      <SplitPane
        split="horizontal"
        minSize={50}
        defaultSize="65%"
        style={{ height: '100vh' }}
        onChange={onLeftBottomPaneResize}
      >
        <div style={{ height: '100%', width: '100%', display: 'flex', flexDirection: 'column' }}>
          {leftComponent}
        </div>
        <div style={{ height: '100%', width: '100%', display: 'flex', flexDirection: 'column' }}>
          {leftBottomComponent}
        </div>
      </SplitPane>
      <div style={{ height: '100%', display: 'flex', flexDirection: 'column' }}>
        {rightComponent}
      </div>
    </SplitPane>
  </>
);

export default ResizableLayoutMui;
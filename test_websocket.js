const WebSocket = require('ws');
const fs = require('fs');

// Read the test graph JSON
const graphData = JSON.parse(fs.readFileSync('/home/aoru/github/ewdhp/MServer/test_graph.json', 'utf8'));

// Create WebSocket connection
const ws = new WebSocket('wss://localhost:5001', {
    rejectUnauthorized: false // Ignore SSL certificate errors for testing
});

ws.on('open', function open() {
    console.log('🔗 Connected to MServer WebSocket');
    console.log('📤 Sending graph execution request...\n');
    
    // Send the graph execution JSON
    ws.send(JSON.stringify(graphData));
});

ws.on('message', function message(data) {
    try {
        const response = JSON.parse(data.toString());
        console.log('📥 Received:', JSON.stringify(response, null, 2));
    } catch (e) {
        console.log('📥 Received (raw):', data.toString());
    }
});

ws.on('error', function error(err) {
    console.error('❌ WebSocket error:', err);
});

ws.on('close', function close() {
    console.log('🔌 Connection closed');
});

// Keep the process alive for a bit to receive responses
setTimeout(() => {
    ws.close();
}, 30000); // 30 seconds timeout
import React, { createContext, useContext, useEffect, useRef } from 'react';
import { Terminal } from 'xterm';
import 'xterm/css/xterm.css';

// Create Context
const TerminalContext = createContext();

// Provider Component
export const TerminalProvider = ({ children }) => {
    const terminals = useRef(new Map());

    const createTerminal = (id) => {
        if (terminals.current.has(id)) {
            console.warn(`Terminal with ID "${id}" already exists.`);
            return terminals.current.get(id);
        }

        console.log(`Creating terminal with ID "${id}"...`);

        const terminal = new Terminal({
    cursorBlink: true,
    theme: {
        background: '#fff',      // light background
        foreground: '#222',      // dark text
    },
});

        const socket = new WebSocket('wss://localhost:5500');
        const inputBuffer = { current: '' };

        socket.onopen = () => {
            console.log(`WebSocket connection established for terminal "${id}"`);
            terminal.writeln('Connected to WebSocket server');
            socket.send(
                JSON.stringify({
                    type: 'connect',
                    host: 'localhost',
                    username: 'ewd',
                    password: '2020',
                })
            );
            terminal.write('\r\n> ');
            terminal.focus();

            terminal.onData((data) => {
                if (data === '\r') {
                    const sanitizedCommand = inputBuffer.current.trim();
                    if (sanitizedCommand !== '') {
                        socket.send(
                            JSON.stringify({
                                type: 'command',
                                command: sanitizedCommand,
                            })
                        );
                        inputBuffer.current = '';
                    }
                    terminal.write('\r\n> ');
                } else if (data === '\u007F') {
                    if (inputBuffer.current.length > 0) {
                        inputBuffer.current = inputBuffer.current.slice(0, -1);
                        terminal.write('\b \b');
                    }
                } else {
                    inputBuffer.current += data;
                    terminal.write(data);
                }
            });
        };

        socket.onmessage = (event) => {
            try {
                const msg = JSON.parse(event.data);
                switch (msg.type) {
                    case 'output':
                        terminal.write(msg.data);
                        break;
                    case 'connected':
                        terminal.writeln('\r\n[SSH CONNECTED]');
                        terminal.write('\r\n> ');
                        break;
                    case 'disconnected':
                        terminal.writeln('\r\n[SSH DISCONNECTED]');
                        break;
                    case 'error':
                        terminal.writeln(`\r\n[ERROR]: ${msg.message}`);
                        break;
                    case 'status':
                        terminal.writeln(`\r\n[STATUS]: ${msg.message}`);
                        break;
                    default:
                        terminal.write(event.data);
                        break;
                }
            } catch (e) {
                terminal.write(event.data);
            }
        };

        socket.onerror = (error) => {
            console.error(`WebSocket error for terminal "${id}":`, error);
        };

        socket.onclose = () => {
            console.log(`WebSocket connection closed for terminal "${id}"`);
            terminal.writeln('[WebSocket Closed]');
        };

        terminals.current.set(id, { terminal, socket, inputBuffer });

        return { terminal, socket };
    };

    const getTerminal = (id) => {
        return terminals.current.get(id);
    };

    const getAllTerminals = () => {
        return Array.from(terminals.current.keys()); // Return all terminal IDs
    };

    const disposeTerminal = (id) => {
        const terminalData = terminals.current.get(id);
        if (terminalData) {
            console.log(`Disposing terminal with ID "${id}"...`);
            terminalData.terminal.dispose();
            terminalData.socket.close();
            terminals.current.delete(id);
        } else {
            console.warn(`Terminal with ID "${id}" does not exist.`);
        }
    };

    return (
        <TerminalContext.Provider
            value={{
                createTerminal,
                getTerminal,
                getAllTerminals, // Expose the method to get all terminal IDs
                disposeTerminal,
            }}
        >
            {children}
        </TerminalContext.Provider>
    );
};

// Custom Hook for Using Context
export const useTerminalSocket = () => {
    const context = useContext(TerminalContext);
    if (!context) {
        throw new Error('useTerminalSocket must be used within a TerminalProvider');
    }
    return context;
};
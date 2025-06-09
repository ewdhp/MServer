import React, { useRef, useEffect, forwardRef, useImperativeHandle } from 'react';
import { useTerminalSocket } from '../context/TerminalProvider';
import { FitAddon } from 'xterm-addon-fit';

function debounce(fn, delay) {
    let timer = null;
    return (...args) => {
        if (timer) clearTimeout(timer);
        timer = setTimeout(() => fn(...args), delay);
    };
}

const SingleTerminal = forwardRef(({ terminalId = "main", resizeSignal }, ref) => {
    const terminalContainerRef = useRef(null);
    const fitAddonRef = useRef(null);
    const terminalInstanceRef = useRef(null);
    const { createTerminal } = useTerminalSocket();

    useImperativeHandle(ref, () => ({
        focusTerminal: () => {
            if (terminalInstanceRef.current) {
                terminalInstanceRef.current.focus();
            }
        }
    }));

    // Mount terminal once
    useEffect(() => {
        if (!terminalContainerRef.current) return;

        const { terminal } = createTerminal(terminalId);
        const fitAddon = new FitAddon();
        terminal.loadAddon(fitAddon);
        terminal.open(terminalContainerRef.current);
        fitAddon.fit();

        fitAddonRef.current = fitAddon;
        terminalInstanceRef.current = terminal;

        // Focus after mount
        setTimeout(() => terminal.focus(), 0);

        return () => {
            terminal.dispose();
        };
    }, [createTerminal, terminalId]);

    // Debounced fit and focus
    const fitAndFocus = React.useCallback(
        debounce(() => {
            if (fitAddonRef.current) fitAddonRef.current.fit();
            if (terminalInstanceRef.current) {
                terminalInstanceRef.current.refresh(0, terminalInstanceRef.current.rows - 1);
                setTimeout(() => terminalInstanceRef.current.focus(), 0);
            }
        }, 100),
        []
    );

    useEffect(() => {
        fitAndFocus();
        window.addEventListener('resize', fitAndFocus);
        return () => window.removeEventListener('resize', fitAndFocus);
    }, [resizeSignal, fitAndFocus]);

    return (
        <div
            ref={terminalContainerRef}
            style={{ width: '100%', height: '100%', background: '#181a1b' }}
        />
    );
});

export default SingleTerminal;
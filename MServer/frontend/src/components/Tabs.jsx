import React, { useState, useEffect } from 'react';
import { Tabs, Tab, Box, Button } from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import SigmaGraph from './SigmaGraph'; // Adjust the import path as necessary   
const CTabs = ({ tabs, renderTabContent, onAddTab, onTabChange }) => {
    const [activeTab, setActiveTab] = useState(0); // Track the active tab

    useEffect(() => {
        // Ensure the active tab index is valid when tabs change
        if (activeTab >= tabs.length) {
            setActiveTab(tabs.length - 1);
        }
    }, [tabs, activeTab]);

    const handleTabChange = (event, newValue) => {
        setActiveTab(newValue); // Update active tab when clicked
        if (onTabChange) {
            onTabChange(newValue); // Notify parent of tab change
        }
    };

    const handleAddTab = () => {
        onAddTab(); // Call the parent-provided function to add a new tab
    };

    return (
        <Box sx={{ width: '100%', height: '100%', display: 'flex', flexDirection: 'column' }}>
            {/* Tabs Section at the Top */}
            <Box sx={{ display: 'flex', alignItems: 'center', padding: '8px', borderBottom: '1px solid #ccc' }}>
                <Tabs
                    value={activeTab}
                    onChange={handleTabChange}
                    variant="scrollable"
                    scrollButtons="auto"
                    aria-label="Dynamic Tabs"
                    sx={{ flexGrow: 1 }} // Make tabs take the available width
                >
                    {tabs.map((tab, index) => (
                        <Tab key={tab.id} label={tab.label} />
                    ))}
                </Tabs>
                <Button
                    onClick={handleAddTab}
                    variant="contained"
                    color="primary"
                    startIcon={<AddIcon />}
                    sx={{ marginLeft: 2 }}
                >
                    Add Tab
                </Button>
            </Box>

            {/* Tab Content Section */}
            <Box
                sx={{
                    flexGrow: 1, // Allow tab content to take remaining height
                    backgroundColor: 'white',
                    overflow: 'hidden',
                    padding: '10px',
                }}
            >
                {tabs.map((tab, index) => (
                    <Box
                        key={tab.id}
                        sx={{
                            display: activeTab === index ? 'block' : 'none', // Show only active tab content
                            width: '100%',
                            height: '100%',
                        }}
                    >
                        {renderTabContent(tab, index)}

                    </Box>
                ))}
            </Box>
        </Box>
    );
};

export default CTabs;
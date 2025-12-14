// JavaScript interop for vis-network graph rendering
window.renderGraph = function(graphDataJson) {
    console.log('renderGraph called with data:', typeof graphDataJson);
    
    // Check if vis library is loaded
    if (typeof vis === 'undefined') {
        console.error('vis-network library is not loaded yet');
        // Retry after a short delay
        setTimeout(() => window.renderGraph(graphDataJson), 100);
        return;
    }
    
    let graphData;
    try {
        graphData = typeof graphDataJson === 'string' ? JSON.parse(graphDataJson) : graphDataJson;
    } catch (e) {
        console.error('Error parsing graph data:', e);
        return;
    }
    
    const container = document.getElementById('network');
    if (!container) {
        console.error('Network container not found');
        return;
    }

    console.log('Container found, rendering graph with', graphData.nodes?.length, 'nodes');

    // Find all node IDs that have edges
    const connectedNodeIds = new Set();
    graphData.edges.forEach(edge => {
        connectedNodeIds.add(edge.from);
        connectedNodeIds.add(edge.to);
    });

    console.log('Total edges:', graphData.edges.length);
    console.log('Connected node IDs:', Array.from(connectedNodeIds));

    // Filter nodes to only include connected nodes or root packages
    const filteredNodes = graphData.nodes.filter(node => 
        node.isRoot || connectedNodeIds.has(node.id)
    );

    console.log('Filtered to', filteredNodes.length, 'connected nodes from', graphData.nodes.length, 'total nodes');
    
    // Log any nodes that were filtered out
    const filteredOutNodes = graphData.nodes.filter(node => 
        !node.isRoot && !connectedNodeIds.has(node.id)
    );
    if (filteredOutNodes.length > 0) {
        console.log('Filtered out nodes (no edges):', filteredOutNodes.map(n => `${n.label}@${n.version} (id:${n.id})`));
    }

    const nodes = new vis.DataSet(filteredNodes.map(node => ({
        id: node.id,
        label: `${node.label}\n${node.version || ''}`,
        color: node.isRoot ? '#667eea' : 
               !node.isFoundInRepository ? '#ff4444' :
               node.reachedMaxDepth ? '#ff9800' : '#48bb78',
        shape: 'box',
        font: { color: '#ffffff', size: 14 },
        borderWidth: !node.isFoundInRepository ? 3 : 1,
        borderWidthSelected: 4
    })));

    const edges = new vis.DataSet(graphData.edges.map(edge => ({
        from: edge.from,
        to: edge.to,
        arrows: 'to',
        color: { color: '#64b5f6', highlight: '#1976d2' }
    })));

    const data = { nodes, edges };
    
    const options = {
        layout: {
            hierarchical: {
                enabled: false
            }
        },
        physics: {
            enabled: true,
            stabilization: {
                enabled: true,
                iterations: 100
            },
            barnesHut: {
                gravitationalConstant: -8000,
                centralGravity: 0.3,
                springLength: 150,
                springConstant: 0.04
            }
        },
        interaction: {
            hover: true,
            navigationButtons: true,
            keyboard: true
        },
        nodes: {
            font: {
                multi: true,
                bold: '14px arial #ffffff'
            }
        },
        edges: {
            color: {
                color: '#64b5f6',
                highlight: '#1976d2',
                hover: '#90caf9'
            }
        }
    };

    if (window.network && typeof window.network.destroy === 'function') {
        console.log('Destroying existing network');
        try {
            window.network.destroy();
        } catch (e) {
            console.warn('Error destroying network:', e);
        }
    }

    console.log('Creating new vis.Network');
    window.network = new vis.Network(container, data, options);
    
    console.log('Applying radial layout');
    // Apply radial layout
    applyRadialLayout(nodes, edges);
    
    console.log('Fitting network to view');
    window.network.fit({
        animation: {
            duration: 1000,
            easingFunction: 'easeInOutQuad'
        }
    });
    
    console.log('Graph rendering complete');
};

function applyRadialLayout(nodes, edges) {
    const nodeArray = nodes.get();
    const edgeArray = edges.get();
    const nodeMap = {};
    const adjacencyList = {};

    nodeArray.forEach(node => {
        nodeMap[node.id] = node;
        adjacencyList[node.id] = [];
    });

    const incomingEdges = {};
    nodeArray.forEach(node => {
        incomingEdges[node.id] = 0;
    });

    edgeArray.forEach(edge => {
        incomingEdges[edge.to] = (incomingEdges[edge.to] || 0) + 1;
        adjacencyList[edge.from].push(edge.to);
    });

    const levels = {};
    const queue = [];

    nodeArray.forEach(node => {
        if (incomingEdges[node.id] === 0) {
            queue.push(node.id);
            levels[node.id] = 0;
        }
    });

    while (queue.length > 0) {
        const nodeId = queue.shift();
        const currentLevel = levels[nodeId];

        adjacencyList[nodeId].forEach(childId => {
            if (!levels[childId] || levels[childId] < currentLevel + 1) {
                levels[childId] = currentLevel + 1;
                if (!queue.includes(childId)) {
                    queue.push(childId);
                }
            }
        });
    }

    const levelGroups = {};
    Object.keys(levels).forEach(nodeId => {
        const level = levels[nodeId];
        if (!levelGroups[level]) {
            levelGroups[level] = [];
        }
        levelGroups[level].push(nodeId);
    });

    const centerX = 0;
    const centerY = 0;
    const baseRadius = 150;
    const radiusIncrement = 180;

    Object.keys(levelGroups).forEach(level => {
        const nodeIds = levelGroups[level];
        const radius = baseRadius + (parseInt(level) * radiusIncrement);
        const angleStep = (2 * Math.PI) / Math.max(nodeIds.length, 1);

        nodeIds.forEach((nodeId, index) => {
            const angle = index * angleStep;
            const x = centerX + radius * Math.cos(angle);
            const y = centerY + radius * Math.sin(angle);

            const existingNode = nodes.get(parseInt(nodeId, 10));

            nodes.update({
                ...existingNode,
                x: x,
                y: y,
                fixed: {
                    x: false,
                    y: false
                }
            });
        });
    });
}

// JavaScript interop for version graph rendering
window.renderVersionGraph = function(graphDataJson) {
    console.log('renderVersionGraph called with data:', typeof graphDataJson);
    
    let data;
    try {
        data = typeof graphDataJson === 'string' ? JSON.parse(graphDataJson) : graphDataJson;
    } catch (e) {
        console.error('Error parsing version graph data:', e);
        return;
    }
    
    const container = document.getElementById('versionNetwork');
    if (!container) {
        console.warn('Version network container not found');
        return;
    }

    console.log('Container found, rendering version graph');

    // Destroy existing network instance
    if (window.versionNetwork && typeof window.versionNetwork.destroy === 'function') {
        console.log('Destroying existing version network');
        try {
            window.versionNetwork.destroy();
        } catch (e) {
            console.warn('Error destroying version network:', e);
        }
    }

    // Group packages by name
    const packageGroups = {};
    data.nodes.forEach(node => {
        const packageName = node.label;
        if (!packageGroups[packageName]) {
            packageGroups[packageName] = [];
        }
        packageGroups[packageName].push(node);
    });

    console.log('Package groups:', packageGroups);

    // Create nodes and edges for the version graph
    const versionNodes = [];
    const versionEdges = [];
    let nodeIdCounter = 0;

    Object.entries(packageGroups).forEach(([packageName, versions]) => {
        // Create parent node for the package
        const parentId = nodeIdCounter++;
        const versionCount = versions.length;
        const uniqueVersions = [...new Set(versions.map(v => v.version))];

        // Determine parent node color
        let parentColor = '#667eea';
        const hasNotFound = versions.some(v => !v.isFoundInRepository);
        const hasMaxDepth = versions.some(v => v.reachedMaxDepth);
        const hasRoot = versions.some(v => v.isRoot);

        if (hasNotFound) {
            parentColor = '#ff4444';
        } else if (hasMaxDepth) {
            parentColor = '#ff9800';
        } else if (hasRoot) {
            parentColor = '#667eea';
        } else {
            parentColor = '#48bb78';
        }

        versionNodes.push({
            id: parentId,
            label: `📦 ${packageName}\n(${uniqueVersions.length} ver)`,
            title: `Package: ${packageName}\nVersions: ${uniqueVersions.join(', ')}\nTotal: ${versionCount}`,
            color: parentColor,
            font: { color: 'white', size: 16, face: 'Arial' },
            borderWidth: 2,
            size: 35,
            shape: 'box'
        });

        // Create child nodes for each version
        versions.forEach(versionNode => {
            const childId = nodeIdCounter++;
            let versionColor, versionLabel;

            if (!versionNode.isFoundInRepository) {
                versionColor = '#ff4444';
                versionLabel = `📌 v${versionNode.version}\n❌`;
            } else if (versionNode.reachedMaxDepth) {
                versionColor = '#ff9800';
                versionLabel = `📌 v${versionNode.version}\n⚠️`;
            } else if (versionNode.isRoot) {
                versionColor = '#667eea';
                versionLabel = `📌 v${versionNode.version}\n⭐`;
            } else {
                versionColor = '#48bb78';
                versionLabel = `📌 v${versionNode.version}`;
            }

            let tooltip = `Version: ${versionNode.version}\n`;
            tooltip += `Type: ${versionNode.isRoot ? 'Root Package' : 'Dependency'}\n`;
            if (!versionNode.isFoundInRepository) {
                tooltip += `Status: ✗ NOT FOUND in npm registry`;
            } else if (versionNode.reachedMaxDepth) {
                tooltip += `Status: ⚠️ Reached max depth`;
            } else {
                tooltip += `Status: ✓ Found in npm registry`;
            }

            versionNodes.push({
                id: childId,
                label: versionLabel,
                title: tooltip,
                color: versionColor,
                font: { color: 'white', size: 12, face: 'Arial' },
                borderWidth: 2,
                size: 20,
                shape: 'box',
                shapeProperties: { borderDashes: !versionNode.isFoundInRepository ? [5, 5] : false }
            });

            // Create edge from parent to child
            versionEdges.push({
                from: parentId,
                to: childId,
                color: '#64b5f6',
                width: 1,
                smooth: { type: 'cubicBezier', forceDirection: 'vertical', roundness: 0.4 }
            });
        });
    });

    console.log('Creating version graph with', versionNodes.length, 'nodes and', versionEdges.length, 'edges');

    const graphData = {
        nodes: new vis.DataSet(versionNodes),
        edges: new vis.DataSet(versionEdges)
    };

    const options = {
        layout: {
            hierarchical: {
                enabled: false
            }
        },
        physics: {
            enabled: true,
            stabilization: {
                enabled: true,
                iterations: 100
            },
            barnesHut: {
                gravitationalConstant: -8000,
                centralGravity: 0.3,
                springLength: 150,
                springConstant: 0.04
            }
        },
        interaction: {
            hover: true,
            navigationButtons: true,
            keyboard: true
        },
        nodes: {
            font: {
                multi: true,
                bold: '14px arial #ffffff'
            }
        },
        edges: {
            color: {
                color: '#64b5f6',
                highlight: '#1976d2',
                hover: '#90caf9'
            }
        }
    };

    console.log('Creating new vis.Network instance for version graph');
    window.versionNetwork = new vis.Network(container, graphData, options);
    console.log('Version network created');

    console.log('Applying radial layout to version graph');
    // Apply radial layout
    applyRadialLayout(graphData.nodes, graphData.edges);
    
    console.log('Fitting version network to view');
    window.versionNetwork.fit({
        animation: {
            duration: 1000,
            easingFunction: 'easeInOutQuad'
        }
    });

    console.log('renderVersionGraph function completed');
};

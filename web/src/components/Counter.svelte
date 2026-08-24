<script lang="ts">
	let count = $state(0);
</script>

<button type="button" onclick={() => count++}>
	Kliknięć: {count}
</button>

<style>
	button {
		font: inherit;
		padding: 0.5rem 1rem;
		border-radius: 8px;
		border: 1px solid #ccc;
		background: #f8f8f8;
		cursor: pointer;
	}
	button:hover {
		background: #eee;
	}
</style>
